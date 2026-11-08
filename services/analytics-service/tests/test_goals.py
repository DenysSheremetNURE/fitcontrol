from datetime import date, timedelta


async def test_get_goal_when_not_set_returns_404(client):
    r = await client.get("/analytics/goals/me")
    assert r.status_code == 404


async def test_set_goal_defaults_valid_from_to_today(client):
    r = await client.put("/analytics/goals/me", json={"daily_calories": 2000})
    assert r.status_code == 200
    assert r.json() == {"daily_calories": 2000, "valid_from": date.today().isoformat()}

    r = await client.get("/analytics/goals/me")
    assert r.json()["daily_calories"] == 2000


async def test_latest_goal_wins(client, set_goal):
    await set_goal(2000, "2026-01-01")
    await set_goal(1800, "2026-06-01")

    r = await client.get("/analytics/goals/me")
    assert r.json() == {"daily_calories": 1800, "valid_from": "2026-06-01"}


async def test_two_goals_same_day_last_one_wins(client, set_goal):
    await set_goal(2000)
    await set_goal(2200)

    r = await client.get("/analytics/goals/me")
    assert r.json()["daily_calories"] == 2200


async def test_future_goal_is_not_current_yet(client, set_goal):
    await set_goal(2000, "2026-01-01")
    await set_goal(1500, (date.today() + timedelta(days=10)).isoformat())

    r = await client.get("/analytics/goals/me")
    assert r.json()["daily_calories"] == 2000


async def test_only_future_goal_means_404(client, set_goal):
    await set_goal(1500, (date.today() + timedelta(days=1)).isoformat())
    r = await client.get("/analytics/goals/me")
    assert r.status_code == 404


async def test_goal_validation(client):
    for value in [0, -100, 10001]:
        r = await client.put("/analytics/goals/me", json={"daily_calories": value})
        assert r.status_code == 422, value
