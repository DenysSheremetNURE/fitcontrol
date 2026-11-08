from sqlalchemy import func, select

from app.db import SessionLocal
from app.tables import DailyStat, MealEntry


async def test_health(client):
    r = await client.get("/health")
    assert r.status_code == 200
    assert r.json() == {"status": "ok", "database": "ok"}


async def test_add_meal_saves_entry_and_creates_daily_row(client, add_meal):
    body = await add_meal("2026-11-05T08:30:00Z", 420, name="Вівсянка", meal_type="breakfast",
                          protein=14, fat=9, carbs=70)
    assert body == {"status": "saved", "day": "2026-11-05"}

    async with SessionLocal() as s:
        meals = (await s.execute(select(MealEntry))).scalars().all()
        assert len(meals) == 1
        assert meals[0].name == "Вівсянка"
        assert meals[0].meal_type == "breakfast"

        stat = await s.get(DailyStat, ("test-user", meals[0].day))
        assert (stat.calories, stat.protein, stat.fat, stat.carbs, stat.meals_count) == (420, 14, 9, 70, 1)


async def test_second_meal_same_day_is_added_to_daily_sum(client, add_meal):
    await add_meal("2026-11-05T08:00:00Z", 400, protein=10, fat=5, carbs=60)
    await add_meal("2026-11-05T19:00:00Z", 600, protein=30, fat=20, carbs=50)

    r = await client.get("/analytics/stats/daily", params={"day": "2026-11-05"})
    assert r.json() == {
        "day": "2026-11-05",
        "calories": 1000, "protein": 40, "fat": 25, "carbs": 110,
        "meals_count": 2,
    }


async def test_meals_on_different_days_go_to_different_rows(client, add_meal):
    await add_meal("2026-11-05T12:00:00Z", 500)
    await add_meal("2026-11-06T12:00:00Z", 700)

    async with SessionLocal() as s:
        assert await s.scalar(select(func.count()).select_from(DailyStat)) == 2


async def test_day_is_taken_from_eaten_at_with_its_timezone(client, add_meal):
    body = await add_meal("2026-11-06T00:30:00+02:00", 300)
    assert body["day"] == "2026-11-06"


async def test_default_values_for_optional_fields(client):
    r = await client.post("/analytics/dev/meals", json={
        "user_id": "test-user",
        "eaten_at": "2026-11-05T12:00:00Z",
        "name": "Щось",
        "calories": 100,
    })
    assert r.status_code == 201
    async with SessionLocal() as s:
        meal = (await s.execute(select(MealEntry))).scalar_one()
        assert meal.meal_type == "other"
        assert (meal.protein, meal.fat, meal.carbs) == (0, 0, 0)


async def test_validation_errors(client):
    base = {"user_id": "u", "eaten_at": "2026-11-05T12:00:00Z", "name": "x", "calories": 100}
    bad_cases = [
        {**base, "calories": -1},
        {**base, "calories": 10001},
        {**base, "protein": -5},
        {**base, "meal_type": "brunch"},
        {**base, "user_id": ""},
        {**base, "name": "x" * 201},
        {**base, "eaten_at": "not-a-date"},
        {k: v for k, v in base.items() if k != "calories"},
    ]
    for body in bad_cases:
        r = await client.post("/analytics/dev/meals", json=body)
        assert r.status_code == 422, body

    async with SessionLocal() as s:
        assert await s.scalar(select(func.count()).select_from(MealEntry)) == 0
