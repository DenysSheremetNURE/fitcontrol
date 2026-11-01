from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    # Береться зі змінної оточення DATABASE_URL (її задає docker-compose)
    database_url: str

    @property
    def async_database_url(self) -> str:
        # docker-compose дає "postgresql://...", а SQLAlchemy для asyncpg потребує "postgresql+asyncpg://..."
        return self.database_url.replace("postgresql://", "postgresql+asyncpg://", 1)


settings = Settings()