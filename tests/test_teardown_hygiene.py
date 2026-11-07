import unittest
import uuid
import time
import sys
from botocore.exceptions import ClientError
from pathlib import Path

# Ensure workspace root is in sys.path
WORKSPACE_ROOT = Path(__file__).resolve().parent.parent
if str(WORKSPACE_ROOT) not in sys.path:
    sys.path.insert(0, str(WORKSPACE_ROOT))

from tests.config import get_sql_connection, get_r2_client, fetch_infisical_secrets


def purge_all_r2_test_artifacts(r2_client=None) -> int:
    """
    Scans all Cloudflare R2 buckets for 'antigravity_test_artifacts/' prefix
    and permanently deletes any residual artifacts. Returns count of deleted objects.
    """
    if r2_client is None:
        r2_client = get_r2_client()

    secrets = fetch_infisical_secrets()
    buckets_to_clean = [
        secrets.get("CloudflareR2__Buckets__Portfolio", "portfolio"),
        secrets.get("CloudflareR2__Buckets__Services", "services")
    ]

    total_purged = 0
    prefix = "antigravity_test_artifacts/"

    for bucket in set(buckets_to_clean):
        try:
            paginator = r2_client.get_paginator("list_objects_v2")
            for page in paginator.paginate(Bucket=bucket, Prefix=prefix):
                contents = page.get("Contents", [])
                for item in contents:
                    key = item["Key"]
                    r2_client.delete_object(Bucket=bucket, Key=key)
                    total_purged += 1
                    print(f" -> Purged residual artifact: {bucket}/{key}")
        except Exception as e:
            print(f" [Warning] Could not scan bucket '{bucket}' for prefix '{prefix}': {e}")

    return total_purged


def purge_all_db_test_mock_data(conn=None) -> int:
    """
    Scans the database for any test probe records and deletes them.
    Returns count of deleted mock rows.
    """
    should_close = False
    if conn is None:
        conn = get_sql_connection()
        should_close = True

    deleted_count = 0
    try:
        cursor = conn.cursor()
        # Clean up any test probe records from dbo.HistoryLog
        cursor.execute("DELETE FROM dbo.HistoryLog WHERE EntityID LIKE 'PRB-%' OR EntityName = 'AntigravityTestProbe';")
        deleted_count += cursor.rowcount
        conn.commit()
        cursor.close()
    finally:
        if should_close:
            conn.close()

    return deleted_count


class TestStrictTeardownAndDataHygiene(unittest.TestCase):
    """
    Automated checks for Strict Teardown (@rule: testing_cleanup):
    - Ensures database mock records are completely rolled back / deleted.
    - Ensures Cloudflare R2 test artifacts are permanently deleted with 0 residuals.
    """

    def setUp(self):
        self.conn = get_sql_connection()
        self.cursor = self.conn.cursor()
        self.r2 = get_r2_client()

    def tearDown(self):
        try:
            self.cursor.close()
            self.conn.close()
        except Exception:
            pass

    def test_01_database_mock_data_rollback_enforced(self):
        """
        Validates that database mock data is strictly rolled back / eradicated
        and verifies via direct SELECT that 0 residual mock rows exist.
        """
        print("\n[Teardown Check] Testing database mock data rollback & hygiene...")
        probe_id = f"PRB-{uuid.uuid4().hex[:6]}"
        probe_entity = "AntigravityTestProbe"

        # 1. Write mock record inside a transaction
        self.conn.autocommit = False
        try:
            self.cursor.execute(
                """
                INSERT INTO dbo.HistoryLog (ActionType, EntityName, EntityID, LoggedAt, ChangedByType)
                VALUES ('CREATE', ?, ?, GETUTCDATE(), 'System');
                """,
                (probe_entity, probe_id)
            )
            self.cursor.execute(
                "SELECT COUNT(*) FROM dbo.HistoryLog WHERE EntityID = ?;",
                (probe_id,)
            )
            inserted_count = self.cursor.fetchone()[0]
            self.assertEqual(inserted_count, 1, "Failed to insert test probe record")
            print(f" -> Inserted test record with ID '{probe_id}'")

            # 2. Rollback transaction (enforcing hygiene rule)
            self.conn.rollback()
            print(" -> Rollback executed.")
        finally:
            self.conn.autocommit = True

        # 3. Verify permanent absence from database
        self.cursor.execute(
            "SELECT COUNT(*) FROM dbo.HistoryLog WHERE EntityID = ?;",
            (probe_id,)
        )
        post_count = self.cursor.fetchone()[0]
        self.assertEqual(
            post_count, 0,
            f"Teardown rule violated: mock record '{probe_id}' remained in database after rollback!"
        )

        # 4. Global database hygiene check
        self.cursor.execute("SELECT COUNT(*) FROM dbo.HistoryLog WHERE EntityID LIKE 'PRB-%';")
        residual_probes = self.cursor.fetchone()[0]
        self.assertEqual(residual_probes, 0, f"Found {residual_probes} residual test probe rows in dbo.HistoryLog")
        print(" -> VERIFIED: Zero residual mock data rows exist in the live database.")

    def test_02_cloudflare_r2_permanent_artifact_deletion_enforced(self):
        """
        Validates that uploaded Cloudflare R2 test artifacts are permanently deleted,
        confirmed via 404 / NoSuchKey on head_object and empty prefix listing.
        """
        print("\n[Teardown Check] Testing Cloudflare R2 permanent artifact deletion & hygiene...")
        secrets = fetch_infisical_secrets()
        bucket = secrets.get("CloudflareR2__Buckets__Portfolio", "portfolio")

        probe_key = f"antigravity_test_artifacts/teardown_probe_{int(time.time())}_{uuid.uuid4().hex[:6]}.dat"
        payload = b"TEARDOWN_HYGIENE_TEST_PAYLOAD"

        # 1. Upload mock artifact
        self.r2.put_object(Bucket=bucket, Key=probe_key, Body=payload)
        print(f" -> Uploaded test artifact '{probe_key}' to bucket '{bucket}'")

        # 2. Confirm existence
        head = self.r2.head_object(Bucket=bucket, Key=probe_key)
        self.assertEqual(head["ContentLength"], len(payload))

        # 3. Execute permanent teardown deletion
        self.r2.delete_object(Bucket=bucket, Key=probe_key)
        print(f" -> Executed delete_object on '{probe_key}'")

        # 4. Verify object is permanently gone (head_object must raise ClientError 404)
        deleted = False
        try:
            self.r2.head_object(Bucket=bucket, Key=probe_key)
        except ClientError as e:
            error_code = e.response.get("Error", {}).get("Code")
            if error_code in ["404", "NoSuchKey", "NotFound"]:
                deleted = True
            else:
                raise

        self.assertTrue(deleted, f"Teardown failed: object '{probe_key}' still accessible via head_object!")
        print(" -> Confirmed object is permanently deleted (head_object returned 404).")

        # 5. Run sweep on test prefix across buckets to ensure zero residual test artifacts remain
        purged = purge_all_r2_test_artifacts(self.r2)
        print(f" -> Hygiene sweep: {purged} additional residual artifacts purged.")

        # 6. Verify prefix listing returns empty
        res = self.r2.list_objects_v2(Bucket=bucket, Prefix="antigravity_test_artifacts/")
        remaining = res.get("KeyCount", 0)
        self.assertEqual(
            remaining, 0,
            f"Teardown rule violated: {remaining} artifacts remain under 'antigravity_test_artifacts/'"
        )
        print(" -> VERIFIED: Cloudflare R2 test artifact directory is 100% clean.")


if __name__ == "__main__":
    unittest.main()
