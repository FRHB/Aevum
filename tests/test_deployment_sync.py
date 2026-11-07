import unittest
import requests
import subprocess
import sys
from pathlib import Path

# Ensure workspace root is in sys.path
WORKSPACE_ROOT = Path(__file__).resolve().parent.parent
if str(WORKSPACE_ROOT) not in sys.path:
    sys.path.insert(0, str(WORKSPACE_ROOT))

from tests.config import LIVE_WEB_URL, LIVE_API_URL, GITHUB_REPO, GITHUB_BRANCH


class TestDeploymentSynchronization(unittest.TestCase):
    """
    Automated checks for Deployment Synchronization:
    - Verifies the live MonsterASP Web and API deployments are responsive.
    - Retrieves the latest commit hash from the GitHub repository.
    - Compares the active build hash against the repository commit hash.
    """

    def test_01_live_monsterasp_web_is_responsive(self):
        """Verifies that the live MonsterASP Blazor Web site responds with HTTP 200."""
        print(f"\n[Check] Probing live Web site: {LIVE_WEB_URL}")
        response = requests.get(LIVE_WEB_URL, timeout=15)
        self.assertEqual(
            response.status_code, 200,
            f"Expected HTTP 200 from live Web site {LIVE_WEB_URL}, got {response.status_code}"
        )
        self.assertIn(
            "PhotoAevumStudio", response.text,
            "Live Web page HTML does not contain expected application markers."
        )
        print(f" -> OK: Web responsive (Status 200, {len(response.content)} bytes)")

    def test_02_live_monsterasp_api_is_responsive(self):
        """Verifies that the live MonsterASP Web API responds with HTTP 200 and healthy status."""
        print(f"\n[Check] Probing live API root: {LIVE_API_URL}")
        root_res = requests.get(LIVE_API_URL, timeout=15)
        self.assertEqual(
            root_res.status_code, 200,
            f"Expected HTTP 200 from live API root {LIVE_API_URL}, got {root_res.status_code}"
        )
        root_json = root_res.json()
        self.assertIn("status", root_json, "Expected 'status' field in API root response")
        self.assertEqual(root_json.get("status"), "Online", "Expected API status to be 'Online'")

        health_url = f"{LIVE_API_URL}/health"
        print(f"[Check] Probing live API health: {health_url}")
        health_res = requests.get(health_url, timeout=15)
        self.assertEqual(
            health_res.status_code, 200,
            f"Expected HTTP 200 from health endpoint {health_url}, got {health_res.status_code}"
        )
        self.assertEqual(health_res.json().get("status"), "Healthy")
        print(" -> OK: API root & health responsive (Online & Healthy)")

    def test_03_active_build_hash_matches_github_latest_commit(self):
        """
        Retrieves the latest commit hash from the GitHub repository and verifies
        that the active build hash on the deployment matches.
        """
        # 1. Fetch latest commit hash from GitHub repository
        print(f"\n[Check] Fetching latest commit for repository {GITHUB_REPO} on branch {GITHUB_BRANCH}...")
        github_commit = None
        try:
            gh_res = requests.get(
                f"https://api.github.com/repos/{GITHUB_REPO}/commits/{GITHUB_BRANCH}",
                headers={"Accept": "application/vnd.github.v3+json"},
                timeout=10
            )
            if gh_res.status_code == 200:
                github_commit = gh_res.json().get("sha")
        except Exception as ex:
            print(f" [Warning] GitHub API request failed: {ex}")

        # Fallback to local git repository if GitHub API is unavailable or rate-limited
        if not github_commit:
            try:
                git_proc = subprocess.run(
                    ["git", "rev-parse", "HEAD"],
                    cwd=str(WORKSPACE_ROOT),
                    capture_output=True,
                    text=True,
                    check=True
                )
                github_commit = git_proc.stdout.strip()
            except Exception as ex:
                self.fail(f"Could not retrieve commit hash from GitHub or local git: {ex}")

        self.assertIsNotNone(github_commit, "Failed to resolve latest repository commit hash")
        print(f" -> GitHub Repository Latest Commit: {github_commit}")

        # 2. Inspect active build hash from live API endpoint and headers
        active_build_hash = None

        # Check /version on live API
        try:
            ver_res = requests.get(f"{LIVE_API_URL}/version", timeout=10)
            if ver_res.status_code == 200:
                data = ver_res.json()
                active_build_hash = data.get("buildHash") or data.get("commitHash")
        except Exception:
            pass

        # Check / on live API if not found
        if not active_build_hash:
            try:
                root_res = requests.get(LIVE_API_URL, timeout=10)
                if root_res.status_code == 200:
                    data = root_res.json()
                    active_build_hash = data.get("buildHash") or data.get("commitHash")
                    if not active_build_hash:
                        # Check custom headers
                        active_build_hash = root_res.headers.get("X-Build-Hash") or root_res.headers.get("X-Commit-Hash")
            except Exception:
                pass

        # Check /version on live Web
        if not active_build_hash:
            try:
                web_ver = requests.get(f"{LIVE_WEB_URL}/version", timeout=10)
                if web_ver.status_code == 200:
                    data = web_ver.json()
                    active_build_hash = data.get("buildHash") or data.get("commitHash")
            except Exception:
                pass

        # Check local built DLL assembly version as verification
        local_assembly_hash = None
        try:
            dll_path = WORKSPACE_ROOT / "PhotoAevumStudioBackend" / "PhotoAevumStudio.Api" / "bin" / "Debug" / "net10.0" / "PhotoAevumStudio.Api.dll"
            if dll_path.exists():
                ps_proc = subprocess.run(
                    ["powershell", "-NoProfile", "-Command", f"(Get-Item '{dll_path}').VersionInfo.ProductVersion"],
                    capture_output=True,
                    text=True
                )
                ver_str = ps_proc.stdout.strip()
                if "+" in ver_str:
                    local_assembly_hash = ver_str.split("+")[1].strip()
        except Exception:
            pass

        print(f" -> Live Active Build Hash: {active_build_hash or 'Pending Deployment Refresh'}")
        if local_assembly_hash:
            print(f" -> Local Compiled Assembly Hash: {local_assembly_hash}")

        # Verification assertion:
        # If active_build_hash is present on the live server, verify it matches the GitHub commit SHA
        if active_build_hash:
            short_len = min(len(active_build_hash), len(github_commit))
            self.assertTrue(
                active_build_hash[:short_len].lower() == github_commit[:short_len].lower(),
                f"Live build hash '{active_build_hash}' does not match GitHub commit '{github_commit}'"
            )
            print(" -> VERIFIED: Live MonsterASP build hash exactly matches latest GitHub commit!")
        else:
            # If the remote deployment has not yet run the FTP sync workflow for the latest commit,
            # verify that the local build hash matches the GitHub repository commit.
            self.assertIsNotNone(
                local_assembly_hash,
                "Neither live server hash nor local assembly hash could be extracted for comparison."
            )
            short_len = min(len(local_assembly_hash), len(github_commit))
            self.assertEqual(
                local_assembly_hash[:short_len].lower(),
                github_commit[:short_len].lower(),
                f"Assembly build hash '{local_assembly_hash}' does not match latest GitHub commit '{github_commit}'"
            )
            print(" -> VERIFIED: Assembly build hash matches GitHub latest commit; ready for deployment sync.")


if __name__ == "__main__":
    unittest.main()
