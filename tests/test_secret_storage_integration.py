import unittest
import hashlib
import uuid
import time
import sys
from pathlib import Path

# Ensure workspace root is in sys.path
WORKSPACE_ROOT = Path(__file__).resolve().parent.parent
if str(WORKSPACE_ROOT) not in sys.path:
    sys.path.insert(0, str(WORKSPACE_ROOT))

from tests.config import fetch_infisical_secrets, get_r2_client


class TestSecretAndStorageIntegration(unittest.TestCase):
    """
    Automated checks for Secret & Storage Integration:
    - Validates that configuration keys are dynamically retrieved from Infisical.
    - Executes a mock image upload and retrieval process using Cloudflare R2 bucket.
    - Enforces teardown cleanup of uploaded test artifacts.
    """

    def setUp(self):
        self.uploaded_keys = []
        self.r2 = get_r2_client()

    def tearDown(self):
        # Strict teardown of all artifacts created during testing
        for bucket, key in self.uploaded_keys:
            try:
                self.r2.delete_object(Bucket=bucket, Key=key)
                print(f" -> Teardown: Deleted Cloudflare R2 test artifact '{key}' from bucket '{bucket}'")
            except Exception as e:
                print(f" [Warning] Teardown failed for R2 artifact '{key}': {e}")

    def test_01_infisical_secrets_pulled_successfully(self):
        """Validates that configuration keys are successfully retrieved from Infisical."""
        print("\n[Infisical Check] Fetching and validating secrets from Infisical...")
        secrets = fetch_infisical_secrets(force_refresh=True)
        self.assertIsInstance(secrets, dict, "Expected secrets to be returned as a dictionary")
        self.assertGreater(len(secrets), 10, f"Expected more than 10 secrets, found {len(secrets)}")

        # Mandatory keys required by Photo Aevum Studio architecture
        expected_keys = [
            "CloudflareR2__AccessKey",
            "CloudflareR2__SecretKey",
            "CloudflareR2__ServiceUrl",
            "CloudflareR2__AccountId",
            "Jwt__Key",
            "EMAIL_CONFIG__PRIMARY__HOST",
            "EMAIL_CONFIG__PRIMARY__USERNAME",
            "EMAIL_CONFIG__PRIMARY__PASSWORD",
            "GoogleMeetSettings__ClientId",
            "GoogleMeetSettings__ClientSecret",
            "XenditSettings__SecretKey",
            "XenditSettings__WebhookToken"
        ]

        for k in expected_keys:
            self.assertIn(k, secrets, f"Critical secret key '{k}' missing from Infisical vault")
            val = secrets.get(k)
            self.assertTrue(
                val and len(val.strip()) > 0,
                f"Secret key '{k}' exists in Infisical but has empty or whitespace value"
            )

        print(f" -> OK: Successfully verified {len(expected_keys)} critical configuration keys from Infisical.")

    def test_02_cloudflare_r2_mock_image_upload_and_retrieval(self):
        """
        Executes a mock image upload and retrieval process using the Cloudflare R2 bucket.
        Verifies byte fidelity and SHA-256 checksum matching.
        """
        print("\n[Storage Check] Executing mock image upload & retrieval on Cloudflare R2...")
        secrets = fetch_infisical_secrets()
        bucket_name = secrets.get("CloudflareR2__Buckets__Portfolio", "portfolio")

        # Generate a minimal valid PNG binary payload
        # Standard 1x1 transparent PNG signature and chunks
        png_header = b"\x89PNG\r\n\x1a\n"
        ihdr_chunk = b"\x00\x00\x00\rIHDR\x00\x00\x00\x01\x00\x00\x00\x01\x08\x06\x00\x00\x00\x1f\x15c4"
        # Unique test probe payload embedding uuid and timestamp for non-colliding checksum
        probe_uuid = str(uuid.uuid4())
        timestamp = str(int(time.time()))
        text_payload = f"ANTIGRAVITY_TEST_IMAGE_PROBE_{probe_uuid}_{timestamp}".encode("utf-8")
        text_chunk_len = len(text_payload) + 5
        text_chunk = text_chunk_len.to_bytes(4, "big") + b"tEXt" + b"Test\x00" + text_payload + b"\x00\x00\x00\x00"
        iend_chunk = b"\x00\x00\x00\x00IEND\xaeB`\x82"
        mock_image_bytes = png_header + ihdr_chunk + text_chunk + iend_chunk

        uploaded_checksum = hashlib.sha256(mock_image_bytes).hexdigest()
        object_key = f"antigravity_test_artifacts/mock_probe_{timestamp}_{probe_uuid[:8]}.png"

        print(f" -> Uploading mock image: {object_key} ({len(mock_image_bytes)} bytes) to bucket '{bucket_name}'...")
        self.r2.put_object(
            Bucket=bucket_name,
            Key=object_key,
            Body=mock_image_bytes,
            ContentType="image/png",
            Metadata={"probe_uuid": probe_uuid, "test_suite": "antigravity"}
        )
        # Register for strict teardown
        self.uploaded_keys.append((bucket_name, object_key))
        print(" -> Upload complete.")

        # Retrieve (download) the object back from Cloudflare R2
        print(f" -> Retrieving mock image: {object_key}...")
        get_response = self.r2.get_object(Bucket=bucket_name, Key=object_key)
        downloaded_bytes = get_response["Body"].read()
        downloaded_checksum = hashlib.sha256(downloaded_bytes).hexdigest()

        # Assert byte integrity and checksum equality
        self.assertEqual(len(downloaded_bytes), len(mock_image_bytes), "Retrieved image size differs from uploaded size")
        self.assertEqual(downloaded_checksum, uploaded_checksum, "Retrieved image SHA-256 checksum mismatch")
        self.assertEqual(get_response.get("ContentType"), "image/png", "Unexpected ContentType on retrieved object")
        print(f" -> Retrieved successfully ({len(downloaded_bytes)} bytes, SHA-256: {downloaded_checksum[:16]}...)")
        print(" -> VERIFIED: Mock image byte integrity and roundtrip storage confirmed.")


if __name__ == "__main__":
    unittest.main()
