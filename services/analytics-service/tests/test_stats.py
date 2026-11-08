from datetime import date, timedelta


async def test_daily_without_data_returns_zeros(client):
    r = await client.get("/analytics/stats/daily", params={"day": "2026-01-01"})
    assert r.status_code == 200
    assert r.json() == {
        "day": "2026-01-01", "calories": 0, "protein": 0, "fat": 0, "carbs": 0, "meals_count": 0,
    }


async def test_daily_defaults_to_today(client, add_meal):
    today = date.today()
    await add_meal(f"{today}T12:00:00", 555)

    r = await client.get("/analytics/stats/daily")
    assert r.json()["day"] == today.isoformat()
    assert r.json()["calories"] == 555


async def test_daily_shows_only_current_user(client, add_meal):
    await add_meal("2026-11-05T12:00:00Z", 500)
    await add_meal("2026-11-05T12:00:00Z", 9999, user_id="other-user")

    r = await client.get("/analytics/stats/daily", params={"day": "2026-11-05"})
    assert r.json()["calories"] == 500
    assert r.json()["meals_count"] == 1


async def test_range_totals_averages_and_macro_percents(client, add_meal):
    await add_meal("2026-11-02T12:00:00Z", 500, protein=25, fat=10, carbs=60)
    await add_meal("2026-11-04T12:00:00Z", 300, protein=15, fat=10, carbs=40)
    await add_meal("2026-11-04T12:00:00Z", 1000, user_id="other-user", protein=100)

    r = await client.get("/analytics/stats/range",
                         params={"date_from": "2026-11-01", "date_to": "2026-11-07"})
    assert r.status_code == 200
    data = r.json()

    assert data["days_logged"] == 2
    assert data["total_calories"] == 800
    assert data["total_protein"] == 40
    assert data["total_fat"] == 20
    assert data["total_carbs"] == 100
    assert data["avg_calories_per_day"] == 400

    assert data["protein_percent"] == 21.6
    assert data["fat_percent"] == 24.3
    assert data["carbs_percent"] == 54.1


async def test_range_lists_every_day_including_empty(client, add_meal):
    await add_meal("2026-11-03T12:00:00Z", 500)

    r = await client.get("/analytics/stats/range",
                         params={"date_from": "2026-11-01", "date_to": "2026-11-07"})
    days = r.json()["days"]
    assert [d["day"] for d in days] == [f"2026-11-0{i}" for i in range(1, 8)]
    assert [d["calories"] for d in days] == [0, 0, 500, 0, 0, 0, 0]


async def test_range_empty_period(client):
    r = await client.get("/analytics/stats/range",
                         params={"date_from": "2026-11-01", "date_to": "2026-11-03"})
    data = r.json()
    assert data["days_logged"] == 0
    assert data["avg_calories_per_day"] == 0
    assert data["protein_percent"] == data["fat_percent"] == data["carbs_percent"] == 0
    assert len(data["days"]) == 3


async def test_range_defaults_to_last_7_days(client):
    today = date.today()
    r = await client.get("/analytics/stats/range")
    data = r.json()
    assert data["date_to"] == today.isoformat()
    assert data["date_from"] == (today - timedelta(days=6)).isoformat()
    assert len(data["days"]) == 7


async def test_range_only_date_to_given(client):
    r = await client.get("/analytics/stats/range", params={"date_to": "2026-11-07"})
    assert r.json()["date_from"] == "2026-11-01"


async def test_range_rejects_reversed_dates(client):
    r = await client.get("/analytics/stats/range",
                         params={"date_from": "2026-11-07", "date_to": "2026-11-01"})
    assert r.status_code == 422


async def test_range_rejects_period_longer_than_year(client):
    r = await client.get("/analytics/stats/range",
                         params={"date_from": "2025-01-01", "date_to": "2026-01-02"})
    assert r.status_code == 422

    r = await client.get("/analytics/stats/range",
                         params={"date_from": "2025-01-01", "date_to": "2026-01-01"})
    assert r.status_code == 200
