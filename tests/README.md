# Antigravity Comprehensive Automated Test Suite
> **Node Type**: Automated Verification & Data Hygiene Specification  
> **Central Node**: [[Project-State]]  
> **Rule Enforcement**: `@rule: testing_cleanup`

---

## 1. Overview & Objectives

The **Antigravity Comprehensive Automated Test Suite** provides end-to-end integration verification, schema validation, and storage diagnostics for [[Photo Aevum Studio]]. It verifies live deployment health, database structural synchronicity, and secret/storage workflows while enforcing strict teardown hygiene.

---

## 2. Test Suite Architecture

| Script / Module | Target Area | Key Automated Checks |
| :--- | :--- | :--- |
| [`test_deployment_sync.py`](file:///C:/Aevum/tests/test_deployment_sync.py) | **Deployment Synchronization** | • Probes live [[MonsterASP.NET]] Blazor Web (`http://photoaevum.tryasp.net/`) and Web API (`http://api-photoaevum.tryasp.net/`, `/health`).<br>• Retrieves latest commit hash from GitHub repository (`FRHB/Aevum`).<br>• Verifies live active build hash matches latest repository commit. |
| [`test_database_validation.py`](file:///C:/Aevum/tests/test_database_validation.py) | **Database Validation** | • Tests read connection to live [[SQL Server]] database (`db77602.public.databaseasp.net`).<br>• Tests write connection via mock record write with atomic transaction rollback.<br>• Validates core live schema against definitions in [[PhotoAevumStudio.sql]], checking all 33 core tables, critical columns, and stored procedures. |
| [`test_secret_storage_integration.py`](file:///C:/Aevum/tests/test_secret_storage_integration.py) | **Secret & Storage Integration** | • Authenticates with [[Infisical]] Universal Auth and validates that critical keys (`CloudflareR2`, `Jwt`, `EmailConfig`, `GoogleMeetSettings`, `XenditSettings`) are retrieved.<br>• Executes synthetic mock PNG image upload and retrieval on [[Cloudflare R2]] bucket.<br>• Validates byte fidelity and SHA-256 checksum matching. |
| [`test_teardown_hygiene.py`](file:///C:/Aevum/tests/test_teardown_hygiene.py) | **Strict Teardown & Hygiene** | • Enforces `@rule: testing_cleanup` from `.agyrules`.<br>• Verifies database mock records are rolled back with zero residual rows.<br>• Permanently deletes all Cloudflare R2 test artifacts and verifies 404 / NoSuchKey on `head_object`. |
| [`run_antigravity_tests.py`](file:///C:/Aevum/tests/run_antigravity_tests.py) | **Master Test Runner** | • Orchestrates all test suites with timing, summary reporting, and an unavoidable master `finally` teardown sweep. |
| [`run_tests.ps1`](file:///C:/Aevum/tests/run_tests.ps1) | **PowerShell Wrapper** | • Windows PowerShell entrypoint for developer and CI automation. |

---

## 3. Execution Instructions

### Running All Tests via Python:
```powershell
python tests/run_antigravity_tests.py
# or
python -m tests
```

### Running All Tests via PowerShell:
```powershell
.\tests\run_tests.ps1
```

### Running Individual Test Suites:
```powershell
python tests/test_deployment_sync.py
python tests/test_database_validation.py
python tests/test_secret_storage_integration.py
python tests/test_teardown_hygiene.py
```

---

## 4. Teardown Hygiene Guarantee (@rule: testing_cleanup)

Every test that interacts with live infrastructure incorporates deterministic cleanup fixtures:
1. **SQL Server**: All test write probes execute within an explicit transaction that rolls back immediately, followed by post-test verification queries asserting zero test records exist.
2. **Cloudflare R2**: All test object keys are registered in the test runner. Upon completion or failure, `delete_object` is executed, verified via `head_object` returning 404, and a prefix purge sweeps any remaining test artifacts.

---
*Back to: [[Project-State]]*
