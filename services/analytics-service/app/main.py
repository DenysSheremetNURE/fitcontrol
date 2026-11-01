from fastapi import FastAPI
from sqlalchemy import text

from app.db import engine

app = FastAPI(
    title="FitControl — Analytics & Reporting Service",
    docs_url="/analytics/docs",
    openapi_url="/analytics/openapi.json",
)


@app.get("/health")
async def health():
    # Робимо найпростіший запит до бази. Якщо база недоступна — буде помилка 500
    async with engine.connect() as conn:
        await conn.execute(text("SELECT 1"))
    return {"status": "ok", "database": "ok"}