import time
from types import SimpleNamespace

import jwt
import pytest
from cryptography.hazmat.primitives.asymmetric import rsa
from fastapi import HTTPException
from fastapi.security import HTTPAuthorizationCredentials

from app import security
from app.config import settings

PRIVATE_KEY = rsa.generate_private_key(public_exponent=65537, key_size=2048)
OTHER_KEY = rsa.generate_private_key(public_exponent=65537, key_size=2048)


def make_token(key=PRIVATE_KEY, **overrides) -> str:
    now = int(time.time())
    claims = {
        "sub": "user-42",
        "iss": settings.jwt_issuer,
        "aud": settings.jwt_audience,
        "iat": now,
        "exp": now + 600,
    }
    claims.update(overrides)
    claims = {k: v for k, v in claims.items() if v is not None}
    return jwt.encode(claims, key, algorithm="RS256", headers={"kid": "test-kid"})


@pytest.fixture(autouse=True)
def fake_jwks(monkeypatch):
    monkeypatch.setattr(
        security.jwks_client,
        "get_signing_key_from_jwt",
        lambda token: SimpleNamespace(key=PRIVATE_KEY.public_key()),
    )


def check(token: str) -> str:
    return security.get_current_user_id(HTTPAuthorizationCredentials(scheme="Bearer", credentials=token))


def assert_rejected(token: str, status: int = 401):
    with pytest.raises(HTTPException) as e:
        check(token)
    assert e.value.status_code == status


def test_valid_token_returns_sub():
    assert check(make_token()) == "user-42"


def test_expired_token():
    assert_rejected(make_token(exp=int(time.time()) - 60))


def test_wrong_signature():
    assert_rejected(make_token(key=OTHER_KEY))


def test_wrong_issuer():
    assert_rejected(make_token(iss="someone-else"))


def test_wrong_audience():
    assert_rejected(make_token(aud="someone-else"))


@pytest.mark.parametrize("claim", ["sub", "exp", "iss", "aud"])
def test_missing_required_claim(claim):
    assert_rejected(make_token(**{claim: None}))


def test_garbage_token(monkeypatch):
    monkeypatch.undo()
    assert_rejected("not-a-jwt")


def test_hs256_token_is_rejected():
    token = jwt.encode(
        {"sub": "hacker", "iss": settings.jwt_issuer, "aud": settings.jwt_audience,
         "exp": int(time.time()) + 600},
        "secret-that-is-at-least-32-bytes-long!!",
        algorithm="HS256",
    )
    assert_rejected(token)


def test_auth_unreachable_gives_503(monkeypatch):
    def boom(token):
        raise jwt.PyJWKClientConnectionError("down")
    monkeypatch.setattr(security.jwks_client, "get_signing_key_from_jwt", boom)
    assert_rejected(make_token(), status=503)



PROTECTED = [
    ("GET", "/analytics/stats/daily"),
    ("GET", "/analytics/stats/range"),
    ("GET", "/analytics/goals/me"),
    ("PUT", "/analytics/goals/me"),
    ("GET", "/analytics/reports/progress"),
    ("GET", "/analytics/reports/habits"),
]


@pytest.mark.parametrize("method,url", PROTECTED)
async def test_protected_endpoints_require_token(anon_client, method, url):
    r = await anon_client.request(method, url, json={"daily_calories": 2000})
    assert r.status_code in (401, 403)


async def test_user_id_is_taken_from_token(anon_client):
    headers = {"Authorization": f"Bearer {make_token(sub='alice')}"}

    r = await anon_client.put("/analytics/goals/me", json={"daily_calories": 1800}, headers=headers)
    assert r.status_code == 200

    r = await anon_client.get("/analytics/goals/me", headers=headers)
    assert r.json()["daily_calories"] == 1800

    bob = {"Authorization": f"Bearer {make_token(sub='bob')}"}
    r = await anon_client.get("/analytics/goals/me", headers=bob)
    assert r.status_code == 404


async def test_invalid_token_over_http(anon_client):
    r = await anon_client.get("/analytics/stats/daily",
                              headers={"Authorization": f"Bearer {make_token(key=OTHER_KEY)}"})
    assert r.status_code == 401
