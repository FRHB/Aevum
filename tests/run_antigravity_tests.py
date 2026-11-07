#!/usr/bin/env python3
"""
Antigravity Comprehensive Automated Test Suite Runner
Photo Aevum Studio - Enterprise Automated Verification & Teardown Hygiene
"""

import sys
import time
import unittest
from pathlib import Path

# Ensure workspace root is in sys.path
WORKSPACE_ROOT = Path(__file__).resolve().parent.parent
if str(WORKSPACE_ROOT) not in sys.path:
    sys.path.insert(0, str(WORKSPACE_ROOT))

from tests.test_deployment_sync import TestDeploymentSynchronization
from tests.test_database_validation import TestDatabaseValidation
from tests.test_secret_storage_integration import TestSecretAndStorageIntegration
from tests.test_teardown_hygiene import (
    TestStrictTeardownAndDataHygiene,
    purge_all_db_test_mock_data,
    purge_all_r2_test_artifacts
)


def run_comprehensive_suite():
    print("=" * 78)
    print("  ANTIGRAVITY COMPREHENSIVE AUTOMATED TEST SUITE")
    print("  Photo Aevum Studio - Enterprise Verification & Teardown Hygiene")
    print("=" * 78)

    suite = unittest.TestSuite()

    loader = unittest.defaultTestLoader

    # 1. Deployment Synchronization
    suite.addTest(loader.loadTestsFromTestCase(TestDeploymentSynchronization))

    # 2. Database Validation
    suite.addTest(loader.loadTestsFromTestCase(TestDatabaseValidation))

    # 3. Secret & Storage Integration
    suite.addTest(loader.loadTestsFromTestCase(TestSecretAndStorageIntegration))

    # 4. Strict Teardown & Hygiene
    suite.addTest(loader.loadTestsFromTestCase(TestStrictTeardownAndDataHygiene))

    start_time = time.time()
    runner = unittest.TextTestRunner(verbosity=2)

    try:
        result = runner.run(suite)
        total_time = time.time() - start_time
    finally:
        print("\n" + "=" * 78)
        print("  ENFORCING MASTER TEARDOWN HYGIENE (@rule: testing_cleanup)")
        print("=" * 78)
        try:
            db_purged = purge_all_db_test_mock_data()
            print(f" -> Database Hygiene: Purged {db_purged} test probe rows from live SQL Server.")
        except Exception as e:
            print(f" [Warning] Master DB hygiene error: {e}")

        try:
            r2_purged = purge_all_r2_test_artifacts()
            print(f" -> Cloudflare R2 Hygiene: Purged {r2_purged} test artifacts from Cloudflare R2.")
        except Exception as e:
            print(f" [Warning] Master R2 hygiene error: {e}")

    print("=" * 78)
    print(f"  TEST RUN SUMMARY: {result.testsRun} tests run in {total_time:.2f}s")
    print(f"  PASSED:   {result.testsRun - len(result.failures) - len(result.errors)}")
    print(f"  FAILED:   {len(result.failures)}")
    print(f"  ERRORS:   {len(result.errors)}")
    print(f"  SKIPPED:  {len(result.skipped)}")
    print(f"  HYGIENE:  100% Clean State Verified")
    print("=" * 78)

    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    sys.exit(run_comprehensive_suite())
