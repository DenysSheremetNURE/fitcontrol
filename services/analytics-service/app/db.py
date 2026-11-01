from sqlalchemy.ext.asyncio import async_sessionmaker, create_async_engine

from app.config import settings

# engine — «двигун»: тримає пул з'єднань з базою
engine = create_async_engine(settings.async_database_url, pool_pre_ping=True)

# SessionLocal — фабрика сесій; сесія = одна «розмова» з базою (запити + транзакція)
SessionLocal = async_sessionmaker(engine, expire_on_commit=False)