from contextlib import asynccontextmanager

from fastapi import FastAPI
from sqlalchemy import text
from app.endpoints import dev
from app.endpoints import dev, stats
from app.endpoints import dev, goals, stats
from app.endpoints import dev, goals, reports, stats

from app import tables
from app.db import Base, engine


@asynccontextmanager
async def lifespan(app: FastAPI):
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield


app = FastAPI(title="FitControl — Analytics Service", lifespan=lifespan)


@app.get("/health")
async def health():
    async with engine.connect() as conn:
        await conn.execute(text("SELECT 1"))
    return {"status": "ok", "database": "ok"}

app.include_router(dev.router)

app.include_router(dev.router)
app.include_router(stats.router)
app.include_router(goals.router)
app.include_router(reports.router)