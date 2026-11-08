from datetime import date, timedelta

import pytest
from fastapi import HTTPException

from app.endpoints.reports import check_period

PERIOD = {"date_from": "2026-11-01", "date_to": "2026-11-07"}



def test_check_period_defaults():
    today = date.today()
    assert check_period(None, None, 30) == (today - timedelta(days=29), today)


def test_check_period_errors():
    with pytest.raises(HTTPException) as e:
        check_period(date(2026, 11, 7), date(2026, 11, 1), 30)
    assert e.value.status_code == 422

    with pytest.raises(HTTPException):
        check_period(date(2025, 1, 1), date(2026, 1, 2), 30)



async def test_progress_without_data(client):
    r = await client.get("/analytics/reports/progress", params=PERIOD)
    assert r.status_code == 200
    data = r.json()
    assert data["days_logged"] == 0
    assert data["days_with_goal"] == 0
    assert data["current_goal"] is None
    assert data["on_target_percent"] is None
    assert data["avg_calories"] is None
    assert data["avg_deviation_percent"] is None
    assert data["current_streak_days"] == 0


async def test_progress_counts_on_target_over_under_and_streak(client, add_meal, set_goal):
    await set_goal(2000, "2026-10-01")
    await add_meal("2026-11-01T12:00:00Z", 2000)
    await add_meal("2026-11-02T12:00:00Z", 2300)
    await add_meal("2026-11-03T12:00:00Z", 1700)
    await add_meal("2026-11-04T12:00:00Z", 2100)
    await add_meal("2026-11-05T12:00:00Z", 1900)

    r = await client.get("/analytics/reports/progress", params=PERIOD)
    data = r.json()

    assert data["current_goal"] == 2000
    assert data["days_logged"] == 5
    assert data["days_with_goal"] == 5
    assert data["days_on_target"] == 3
    assert data["days_over"] == 1
    assert data["days_under"] == 1
    assert data["on_target_percent"] == 60.0
    assert data["avg_calories"] == 2000.0
    assert data["avg_deviation_percent"] == 0.0
    assert data["current_streak_days"] == 2


async def test_progress_tolerance_boundary_is_inclusive(client, add_meal, set_goal):
    await set_goal(2000, "2026-10-01")
    await add_meal("2026-11-01T12:00:00Z", 2200)
    await add_meal("2026-11-02T12:00:00Z", 1800)

    data = (await client.get("/analytics/reports/progress", params=PERIOD)).json()
    assert data["days_on_target"] == 2


async def test_progress_streak_breaks_on_missing_day(client, add_meal, set_goal):
    await set_goal(2000, "2026-10-01")
    await add_meal("2026-11-01T12:00:00Z", 2000)
    await add_meal("2026-11-02T12:00:00Z", 2000)
    await add_meal("2026-11-04T12:00:00Z", 2000)

    data = (await client.get("/analytics/reports/progress", params=PERIOD)).json()
    assert data["days_on_target"] == 3
    assert data["current_streak_days"] == 1


async def test_progress_streak_is_zero_if_last_day_failed(client, add_meal, set_goal):
    await set_goal(2000, "2026-10-01")
    await add_meal("2026-11-01T12:00:00Z", 2000)
    await add_meal("2026-11-02T12:00:00Z", 3000)

    data = (await client.get("/analytics/reports/progress", params=PERIOD)).json()
    assert data["current_streak_days"] == 0


async def test_progress_uses_goal_that_was_valid_on_each_day(client, add_meal, set_goal):
    await set_goal(2000, "2026-11-02")
    await set_goal(1500, "2026-11-04")

    await add_meal("2026-11-01T12:00:00Z", 1000)
    await add_meal("2026-11-03T12:00:00Z", 2000)
    await add_meal("2026-11-05T12:00:00Z", 2000)

    data = (await client.get("/analytics/reports/progress", params=PERIOD)).json()
    assert data["days_logged"] == 3
    assert data["days_with_goal"] == 2
    assert data["days_on_target"] == 1
    assert data["days_over"] == 1
    assert data["current_goal"] == 1500


async def test_progress_ignores_other_users(client, add_meal, set_goal):
    await set_goal(2000, "2026-10-01")
    await add_meal("2026-11-01T12:00:00Z", 2000)
    await add_meal("2026-11-02T12:00:00Z", 5000, user_id="other-user")

    data = (await client.get("/analytics/reports/progress", params=PERIOD)).json()
    assert data["days_logged"] == 1


async def test_progress_rejects_bad_period(client):
    r = await client.get("/analytics/reports/progress",
                         params={"date_from": "2026-11-07", "date_to": "2026-11-01"})
    assert r.status_code == 422



async def test_habits_without_data(client):
    data = (await client.get("/analytics/reports/habits", params=PERIOD)).json()
    assert data["meals_logged"] == 0
    assert data["days_logged"] == 0
    assert data["avg_meals_per_day"] == 0
    assert data["by_meal_type"] == []
    assert data["top_foods"] == []
    assert data["breakfast_skipped_days"] == 0
    assert data["weekday_avg_calories"] is None
    assert data["weekend_avg_calories"] is None


async def test_habits_full_report(client, add_meal):
    assert date(2026, 11, 2).weekday() == 0
    assert date(2026, 11, 7).weekday() == 5

    await add_meal("2026-11-02T08:00:00Z", 400, name="Вівсянка", meal_type="breakfast")
    await add_meal("2026-11-02T13:00:00Z", 600, name="Борщ", meal_type="lunch")
    await add_meal("2026-11-02T19:00:00Z", 500, name="Курка", meal_type="dinner")
    await add_meal("2026-11-03T13:00:00Z", 700, name=" борщ ", meal_type="lunch")
    await add_meal("2026-11-03T16:00:00Z", 100, name="Яблуко", meal_type="snack")
    await add_meal("2026-11-07T09:00:00Z", 400, name="вівсянка", meal_type="breakfast")
    await add_meal("2026-11-07T20:00:00Z", 1200, name="Піца", meal_type="dinner")
    await add_meal("2026-11-02T08:00:00Z", 999, user_id="other-user", name="Борщ", meal_type="lunch")

    data = (await client.get("/analytics/reports/habits", params=PERIOD)).json()

    assert data["meals_logged"] == 7
    assert data["days_logged"] == 3
    assert data["avg_meals_per_day"] == 2.3
    assert data["breakfast_skipped_days"] == 1
    assert data["weekday_avg_calories"] == 1150.0
    assert data["weekend_avg_calories"] == 1600.0

    assert data["by_meal_type"] == [
        {"meal_type": "breakfast", "count": 2, "avg_calories": 400.0, "share_of_calories_percent": 20.5},
        {"meal_type": "lunch", "count": 2, "avg_calories": 650.0, "share_of_calories_percent": 33.3},
        {"meal_type": "dinner", "count": 2, "avg_calories": 850.0, "share_of_calories_percent": 43.6},
        {"meal_type": "snack", "count": 1, "avg_calories": 100.0, "share_of_calories_percent": 2.6},
    ]

    assert data["top_foods"] == [
        {"name": "борщ", "count": 2, "total_calories": 1300.0},
        {"name": "вівсянка", "count": 2, "total_calories": 800.0},
        {"name": "піца", "count": 1, "total_calories": 1200.0},
        {"name": "курка", "count": 1, "total_calories": 500.0},
        {"name": "яблуко", "count": 1, "total_calories": 100.0},
    ]


async def test_habits_top_foods_limited_to_five(client, add_meal):
    for i in range(7):
        await add_meal("2026-11-02T12:00:00Z", 100 + i, name=f"Страва {i}")

    data = (await client.get("/analytics/reports/habits", params=PERIOD)).json()
    assert len(data["top_foods"]) == 5
    assert data["top_foods"][0]["name"] == "страва 6"


async def test_habits_zero_calorie_meals_do_not_crash(client, add_meal):
    await add_meal("2026-11-02T12:00:00Z", 0, name="Вода", meal_type="snack")

    data = (await client.get("/analytics/reports/habits", params=PERIOD)).json()
    assert data["by_meal_type"][0]["share_of_calories_percent"] == 0
