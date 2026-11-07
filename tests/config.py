import os
import json
import requests
import pyodbc
import boto3
from pathlib import Path
from typing import Dict, Any, Optional

WORKSPACE_ROOT = Path(__file__).resolve().parent.parent

# Read appsettings.json for baseline configuration
APPSETTINGS_PATH = WORKSPACE_ROOT / "PhotoAevumStudioBackend" / "PhotoAevumStudio.Api" / "appsettings.json"
APPSETTINGS_DATA: Dict[str, Any] = {}
if APPSETTINGS_PATH.exists():
    try:
        with open(APPSETTINGS_PATH, "r", encoding="utf-8") as f:
            APPSETTINGS_DATA = json.load(f)
    except Exception as e:
        print(f"[Config Warning] Failed reading appsettings.json: {e}")

# Infisical Parameters
INFISICAL_CONFIG = APPSETTINGS_DATA.get("Infisical", {})
INFISICAL_CLIENT_ID = os.getenv("INFISICAL_CLIENT_ID", INFISICAL_CONFIG.get("ClientId", "0f2e5849-86fb-4880-ac55-d1b6ae8552f9"))
INFISICAL_CLIENT_SECRET = os.getenv("INFISICAL_CLIENT_SECRET", INFISICAL_CONFIG.get("ClientSecret", "773cdc3cb3a6bc33efc4fad0c63288ed6452f7d0b78b30fc52646ec87f45fa65"))
INFISICAL_PROJECT_ID = os.getenv("INFISICAL_PROJECT_ID", INFISICAL_CONFIG.get("ProjectId", "26fb5dcd-e66c-4054-8dab-7cac8519bdee"))
INFISICAL_ENVIRONMENT = os.getenv("INFISICAL_ENVIRONMENT", INFISICAL_CONFIG.get("Environment", "dev"))
INFISICAL_BASE_URL = os.getenv("INFISICAL_BASE_URL", "https://app.infisical.com")

# Live MonsterASP URLs
LIVE_WEB_URL = os.getenv("AEVUM_LIVE_WEB_URL", "http://photoaevum.tryasp.net/").rstrip("/")
LIVE_API_URL = os.getenv("AEVUM_LIVE_API_URL", "http://api-photoaevum.tryasp.net/").rstrip("/")

# GitHub Repository Information
GITHUB_REPO = os.getenv("AEVUM_GITHUB_REPO", "FRHB/Aevum")
GITHUB_BRANCH = os.getenv("AEVUM_GITHUB_BRANCH", "main")

# Database Connection Defaults
DB_RAW_CONN = APPSETTINGS_DATA.get("ConnectionStrings", {}).get("DefaultConnection", "")

_SECRETS_CACHE: Optional[Dict[str, str]] = None


def fetch_infisical_secrets(force_refresh: bool = False) -> Dict[str, str]:
    """Authenticates with Infisical via Universal Auth and retrieves all secrets."""
    global _SECRETS_CACHE
    if _SECRETS_CACHE is not None and not force_refresh:
        return _SECRETS_CACHE

    login_url = f"{INFISICAL_BASE_URL}/api/v1/auth/universal-auth/login"
    login_payload = {
        "clientId": INFISICAL_CLIENT_ID,
        "clientSecret": INFISICAL_CLIENT_SECRET
    }

    login_res = requests.post(login_url, json=login_payload, timeout=15)
    login_res.raise_for_status()
    auth_data = login_res.json()
    access_token = auth_data.get("accessToken")
    if not access_token:
        raise ValueError("Infisical login succeeded but returned empty accessToken")

    secrets_url = f"{INFISICAL_BASE_URL}/api/v3/secrets/raw"
    params = {
        "workspaceId": INFISICAL_PROJECT_ID,
        "environment": INFISICAL_ENVIRONMENT
    }
    headers = {"Authorization": f"Bearer {access_token}"}

    sec_res = requests.get(secrets_url, params=params, headers=headers, timeout=15)
    sec_res.raise_for_status()
    raw_secrets = sec_res.json().get("secrets", [])

    secrets_map = {item.get("secretKey"): item.get("secretValue") for item in raw_secrets}
    _SECRETS_CACHE = secrets_map
    return secrets_map


def get_r2_client():
    """Builds and returns a boto3 S3 client targeted at Cloudflare R2."""
    secrets = fetch_infisical_secrets()
    access_key = secrets.get("CloudflareR2__AccessKey") or APPSETTINGS_DATA.get("CloudflareR2", {}).get("AccessKey")
    secret_key = secrets.get("CloudflareR2__SecretKey") or APPSETTINGS_DATA.get("CloudflareR2", {}).get("SecretKey")
    service_url = secrets.get("CloudflareR2__ServiceUrl") or APPSETTINGS_DATA.get("CloudflareR2", {}).get("ServiceUrl")

    if not access_key or not secret_key or not service_url:
        raise ValueError("Missing Cloudflare R2 credentials from Infisical and appsettings")

    s3_client = boto3.client(
        "s3",
        endpoint_url=service_url,
        aws_access_key_id=access_key,
        aws_secret_access_key=secret_key,
        region_name="auto"
    )
    return s3_client


def get_sql_connection():
    """Builds and opens a live SQL Server connection via pyodbc using installed ODBC driver."""
    available_drivers = pyodbc.drivers()
    preferred_driver = None
    for candidate in ["ODBC Driver 18 for SQL Server", "ODBC Driver 17 for SQL Server", "SQL Server"]:
        if candidate in available_drivers:
            preferred_driver = candidate
            break

    if not preferred_driver:
        raise RuntimeError(f"No suitable SQL Server ODBC driver found. Available: {available_drivers}")

    # Parse raw connection string from appsettings
    server = "db77602.public.databaseasp.net"
    database = "db77602"
    uid = "db77602"
    pwd = "G!r7cB8#Z5_d"

    conn_str = (
        f"DRIVER={{{preferred_driver}}};"
        f"SERVER={server};"
        f"DATABASE={database};"
        f"UID={uid};"
        f"PWD={pwd};"
        "Encrypt=yes;"
        "TrustServerCertificate=yes;"
        "Connection Timeout=30;"
    )

    conn = pyodbc.connect(conn_str)
    return conn
