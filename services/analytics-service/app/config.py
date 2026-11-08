from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    database_url: str

    jwks_url: str = "http://auth-service:8080/.well-known/jwks.json"
    jwt_issuer: str
    jwt_audience: str


settings = Settings()