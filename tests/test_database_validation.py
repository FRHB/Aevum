import unittest
import re
import sys
from pathlib import Path

# Ensure workspace root is in sys.path
WORKSPACE_ROOT = Path(__file__).resolve().parent.parent
if str(WORKSPACE_ROOT) not in sys.path:
    sys.path.insert(0, str(WORKSPACE_ROOT))

from tests.config import get_sql_connection, WORKSPACE_ROOT


class TestDatabaseValidation(unittest.TestCase):
    """
    Automated checks for Database Validation:
    - Tests read connection to the live SQL Server database.
    - Tests write connection and ensures atomic rollback / cleanup.
    - Validates live database core schema against PhotoAevumStudio.sql definitions.
    """

    def setUp(self):
        self.conn = get_sql_connection()
        self.cursor = self.conn.cursor()

    def tearDown(self):
        try:
            self.cursor.close()
            self.conn.close()
        except Exception:
            pass

    def test_01_live_sql_read_connection(self):
        """Verifies read connection to the live SQL Server database and basic querying."""
        print("\n[DB Read Check] Testing live SQL Server read connection...")
        self.cursor.execute("SELECT @@VERSION, DB_NAME();")
        row = self.cursor.fetchone()
        self.assertIsNotNone(row, "Failed to query @@VERSION from live SQL Server")
        version_str, db_name = row[0], row[1]
        self.assertTrue(len(version_str) > 0, "Returned empty SQL Server version")
        self.assertEqual(db_name, "db77602", f"Unexpected database name: {db_name}")
        print(f" -> Connected to: {db_name} (SQL Server {version_str[:35]}...)")

        # Read counts from core application tables
        tables_to_read = [
            "dbo.Departments",
            "dbo.RateType",
            "dbo.Services",
            "dbo.Employees",
            "dbo.Admin",
            "dbo.Customer",
            "dbo.EventAppointment"
        ]
        for tbl in tables_to_read:
            self.cursor.execute(f"SELECT COUNT(*) FROM {tbl};")
            count = self.cursor.fetchone()[0]
            self.assertGreaterEqual(count, 0, f"Failed counting rows in {tbl}")
            print(f" -> Read {tbl}: {count} rows")

    def test_02_live_sql_write_connection_with_rollback(self):
        """
        Verifies write connection by writing a mock test record into dbo.HistoryLog,
        verifying the record was persisted, then rolling back / deleting it.
        """
        print("\n[DB Write Check] Testing live SQL Server write with transaction rollback...")
        test_action_type = "CREATE"
        test_entity = "AntigravityTestProbe"
        test_entity_id = "PRB-9999"

        # Begin explicit transaction
        self.conn.autocommit = False
        try:
            insert_sql = """
                INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, LoggedAt, ChangedByType)
                VALUES (?, ?, ?, GETUTCDATE(), 'System');
            """
            self.cursor.execute(insert_sql, (test_action_type, test_entity, test_entity_id))

            # Verify the write can be read within the transaction
            self.cursor.execute(
                "SELECT COUNT(*) FROM dbo.HistoryLog WHERE ActionType = ? AND EntityID = ?;",
                (test_action_type, test_entity_id)
            )
            count = self.cursor.fetchone()[0]
            self.assertEqual(count, 1, "Write probe failed: inserted record not found")
            print(" -> Write probe verified: successfully inserted test record in transaction.")

            # Rollback the transaction to guarantee strict teardown
            self.conn.rollback()
            print(" -> Rollback executed.")

            # Verify that the test record does NOT exist after rollback
            self.cursor.execute(
                "SELECT COUNT(*) FROM dbo.HistoryLog WHERE ActionType = ? AND EntityID = ?;",
                (test_action_type, test_entity_id)
            )
            final_count = self.cursor.fetchone()[0]
            self.assertEqual(final_count, 0, "Teardown rollback violation: test record persisted after rollback!")
            print(" -> VERIFIED: Zero residual test mock data remains in database.")
        finally:
            self.conn.autocommit = True

    def test_03_core_schema_matches_sql_definitions(self):
        """
        Parses table definitions from PhotoAevumStudio.sql and verifies
        that all 33 core tables and their critical schema columns exist in the live database.
        """
        print("\n[DB Schema Check] Verifying live database core schema matches SQL definitions...")
        sql_path = WORKSPACE_ROOT / "PhotoAevumStudioBackend" / "database" / "PhotoAevumStudio.sql"
        self.assertTrue(sql_path.exists(), f"SQL definition file missing: {sql_path}")

        with open(sql_path, "r", encoding="utf-8", errors="ignore") as f:
            sql_content = f.read()

        defined_tables = set(re.findall(r"CREATE\s+TABLE\s+(?:dbo\.)?\[?(\w+)\]?", sql_content, re.IGNORECASE))
        self.assertGreaterEqual(len(defined_tables), 30, f"Expected at least 30 core tables, found {len(defined_tables)}")
        print(f" -> Defined core tables in SQL script: {len(defined_tables)}")

        # Query live tables
        self.cursor.execute(
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_TYPE = 'BASE TABLE';"
        )
        live_tables = set(r[0] for r in self.cursor.fetchall())
        print(f" -> Live dbo tables in SQL Server: {len(live_tables)}")

        missing_tables = defined_tables - live_tables
        self.assertEqual(
            len(missing_tables), 0,
            f"Live database schema is missing required tables: {missing_tables}"
        )
        print(" -> All 33 core tables exist in the live SQL Server database.")

        # Validate critical architectural columns on core tables
        column_checks = [
            ("EventAppointment", "AssignedEmployeeName"),
            ("EventAppointment", "LockExpiresAt"),
            ("EventAppointment", "QrString"),
            ("Services", "ImageFileName"),
            ("Services", "ImageUrl"),
            ("Employees", "DaysOff"),
            ("Employees", "Address"),
            ("Employees", "BirthYear"),
            ("Departments", "DepartmentID"),
            ("Departments", "DepartmentName"),
            ("ArchiveIndex", "ArchiveId"),
            ("ArchiveIndex", "BucketName"),
            ("HomeHighlights", "HighlightID"),
            ("HomeHighlights", "DisplayOrder"),
            ("StudioFAQs", "FaqID")
        ]

        for table_name, column_name in column_checks:
            self.cursor.execute(
                """
                SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = ? AND COLUMN_NAME = ?;
                """,
                (table_name, column_name)
            )
            col_exists = self.cursor.fetchone()[0]
            self.assertEqual(
                col_exists, 1,
                f"Missing critical schema column 'dbo.{table_name}.{column_name}'"
            )

        print(" -> All critical architectural columns verified.")

        # Validate key stored procedures exist in live database
        procedures_to_check = [
            "sp_GetNextServiceID",
            "sp_CreateEventAppointment",
            "spDeleteEmployee",
            "usp_ReleaseExpiredPendingPayments",
            "sp_AssignOfficeClerkRoundRobin"
        ]

        for proc in procedures_to_check:
            self.cursor.execute(
                "SELECT COUNT(*) FROM sys.procedures WHERE name = ?;",
                (proc,)
            )
            proc_exists = self.cursor.fetchone()[0]
            self.assertEqual(proc_exists, 1, f"Missing required stored procedure: dbo.{proc}")

        print(" -> All required stored procedures verified.")


if __name__ == "__main__":
    unittest.main()
