import asyncio
import os

import asyncpg

MAIN_URL = os.environ["DATABASE_URL"]
TEST_DB_NAME = "analytics_db_test"
os.environ["DATABASE_URL"] = MAIN_URL.rsplit("/", 1)[0] + "/" + TEST_DB_NAME


async def _ensure_test_database():
    conn = await asyncpg.connect(MAIN_URL)
    try:
        exists = await conn.fetchval("SELECT 1 FROM pg_database WHERE datname = $1", TEST_DB_NAME)
        if not exists:
            await conn.execute(f'CREATE DATABASE "{TEST_DB_NAME}"')
    finally:
        await conn.close()


asyncio.run(_ensure_test_database())

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient
from pytest_asyncio import is_async_test
from sqlalchemy import text

from app import tables
from app.db import Base, engine
from app.main import app
from app.security import get_current_user_id

TEST_USER = "test-user"


def pytest_collection_modifyitems(items):
    marker = pytest.mark.asyncio(loop_scope="session")
    for item in items:
        if is_async_test(item):
            item.add_marker(marker, append=False)


@pytest_asyncio.fixture(loop_scope="session", scope="session", autouse=True)
async def create_tables():
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)
        await conn.run_sync(Base.metadata.create_all)
    yield
    await engine.dispose()


async def _truncate():
    async with engine.begin() as conn:
        await conn.execute(text(
            "TRUNCATE an_meal_entries, an_daily_stats, an_user_goal_history RESTART IDENTITY"
        ))


@pytest_asyncio.fixture(loop_scope="session")
async def client():
    await _truncate()

    app.dependency_overrides[get_current_user_id] = lambda: TEST_USER

    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as c:
        yield c

    app.dependency_overrides.clear()


@pytest_asyncio.fixture(loop_scope="session")
async def anon_client():
    await _truncate()
    app.dependency_overrides.clear()
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as c:
        yield c


@pytest.fixture
def add_meal(client):
    async def _add(eaten_at: str, calories: float, *, user_id: str = TEST_USER, name: str = "Їжа",
                   meal_type: str = "other", protein: float = 0, fat: float = 0, carbs: float = 0):
        r = await client.post("/analytics/dev/meals", json={
            "user_id": user_id,
            "eaten_at": eaten_at,
            "meal_type": meal_type,
            "name": name,
            "calories": calories,
            "protein": protein,
            "fat": fat,
            "carbs": carbs,
        })
        assert r.status_code == 201, r.text
        return r.json()
    return _add


@pytest.fixture
def set_goal(client):
    async def _set(daily_calories: float, valid_from: str | None = None):
        body = {"daily_calories": daily_calories}
        if valid_from:
            body["valid_from"] = valid_from
        r = await client.put("/analytics/goals/me", json=body)
        assert r.status_code == 200, r.text
        return r.json()
    return _set
