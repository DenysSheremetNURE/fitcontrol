import jwt
from fastapi import Depends, HTTPException
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer

from app.config import settings

bearer = HTTPBearer()
jwks_client = jwt.PyJWKClient(settings.jwks_url, cache_keys=True, lifespan=300)


def get_current_user_id(creds: HTTPAuthorizationCredentials = Depends(bearer)) -> str:
    token = creds.credentials
    try:
        key = jwks_client.get_signing_key_from_jwt(token)
        claims = jwt.decode(
            token,
            key.key,
            algorithms=["RS256"],
            issuer=settings.jwt_issuer,
            audience=settings.jwt_audience,
            options={"require": ["exp", "sub", "iss", "aud"]},
        )
    except jwt.ExpiredSignatureError:
        raise HTTPException(401, "Токен прострочений")
    except jwt.PyJWKClientConnectionError:
        raise HTTPException(503, "Не вдалося отримати ключі від Auth")
    except (jwt.InvalidTokenError, jwt.PyJWKClientError):
        raise HTTPException(401, "Недійсний токен")

    return claims["sub"]