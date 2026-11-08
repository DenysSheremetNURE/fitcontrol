from collections import defaultdict
from datetime import date, timedelta

from fastapi import APIRouter, Depends, HTTPException, Query
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.db import get_session
from app.schemes import HabitsReport, MealTypeHabit, ProgressReport, TopFood
from app.security import get_current_user_id
from app.tables import DailyStat, GoalHistory, MealEntry

router = APIRouter(prefix="/analytics/reports", tags=["Звіти"])

TOLERANCE_PERCENT = 10 


def check_period(date_from: date | None, date_to: date | None, default_days: int) -> tuple[date, date]:
    date_to = date_to or date.today()
    date_from = date_from or date_to - timedelta(days=default_days - 1)
    if date_from > date_to:
        raise HTTPException(422, "date_from не може бути пізніше за date_to")
    if (date_to - date_from).days > 365:
        raise HTTPException(422, "Період не може бути довшим за рік")
    return date_from, date_to


async def goals_by_day(session: AsyncSession, user_id: str, date_from: date, date_to: date) -> dict[date, float]:
    result = await session.execute(
        select(GoalHistory)
        .where(GoalHistory.user_id == user_id, GoalHistory.valid_from <= date_to)
        .order_by(GoalHistory.valid_from, GoalHistory.id)
    )
    goals = result.scalars().all()

    by_day = {}
    current = None
    i = 0
    d = date_from
    while d <= date_to:
        while i < len(goals) and goals[i].valid_from <= d:
            current = goals[i].daily_calories
            i += 1
        if current is not None:
            by_day[d] = current
        d += timedelta(days=1)
    return by_day


@router.get("/progress", response_model=ProgressReport)
async def progress_report(
    user_id: str = Depends(get_current_user_id),
    date_from: date | None = Query(None),
    date_to: date | None = Query(None),
    session: AsyncSession = Depends(get_session),
):
    date_from, date_to = check_period(date_from, date_to, default_days=30)

    result = await session.execute(
        select(DailyStat).where(
            DailyStat.user_id == user_id,
            DailyStat.day.between(date_from, date_to),
        )
    )
    rows = {row.day: row for row in result.scalars().all()}
    goals = await goals_by_day(session, user_id, date_from, date_to)

    on_target = over = under = 0
    deviations = []
    good_days = set()
    for d, row in rows.items():
        goal = goals.get(d)
        if goal is None:
            continue
        deviation = (row.calories - goal) / goal * 100
        deviations.append(deviation)
        if deviation > TOLERANCE_PERCENT:
            over += 1
        elif deviation < -TOLERANCE_PERCENT:
            under += 1
        else:
            on_target += 1
            good_days.add(d)

    streak = 0
    if rows:
        d = max(rows)
        while d in good_days:
            streak += 1
            d -= timedelta(days=1)

    days_with_goal = len(deviations)
    return ProgressReport(
        date_from=date_from,
        date_to=date_to,
        current_goal=goals.get(date_to),
        days_logged=len(rows),
        days_with_goal=days_with_goal,
        days_on_target=on_target,
        days_over=over,
        days_under=under,
        on_target_percent=round(on_target / days_with_goal * 100, 1) if days_with_goal else None,
        avg_calories=round(sum(r.calories for r in rows.values()) / len(rows), 1) if rows else None,
        avg_deviation_percent=round(sum(deviations) / days_with_goal, 1) if days_with_goal else None,
        current_streak_days=streak,
    )


@router.get("/habits", response_model=HabitsReport)
async def habits_report(
    user_id: str = Depends(get_current_user_id),
    date_from: date | None = Query(None),
    date_to: date | None = Query(None),
    session: AsyncSession = Depends(get_session),
):
    date_from, date_to = check_period(date_from, date_to, default_days=30)

    result = await session.execute(
        select(MealEntry).where(
            MealEntry.user_id == user_id,
            MealEntry.day.between(date_from, date_to),
        )
    )
    meals = result.scalars().all()

    meal_types_by_day = defaultdict(set)
    calories_by_day = defaultdict(float)
    calories_by_type = defaultdict(list)
    calories_by_food = defaultdict(list)
    for m in meals:
        meal_types_by_day[m.day].add(m.meal_type)
        calories_by_day[m.day] += m.calories
        calories_by_type[m.meal_type].append(m.calories)
        calories_by_food[m.name.strip().lower()].append(m.calories)

    total_calories = sum(m.calories for m in meals)
    days_logged = len(meal_types_by_day)

    by_meal_type = []
    for meal_type in ["breakfast", "lunch", "dinner", "snack", "other"]:
        values = calories_by_type.get(meal_type)
        if not values:
            continue
        by_meal_type.append(MealTypeHabit(
            meal_type=meal_type,
            count=len(values),
            avg_calories=round(sum(values) / len(values), 1),
            share_of_calories_percent=round(sum(values) / total_calories * 100, 1) if total_calories else 0,
        ))

    top = sorted(calories_by_food.items(), key=lambda item: (-len(item[1]), -sum(item[1])))[:5]
    top_foods = [TopFood(name=name, count=len(v), total_calories=round(sum(v), 1)) for name, v in top]

    weekday = [cal for d, cal in calories_by_day.items() if d.weekday() < 5]
    weekend = [cal for d, cal in calories_by_day.items() if d.weekday() >= 5]

    return HabitsReport(
        date_from=date_from,
        date_to=date_to,
        meals_logged=len(meals),
        days_logged=days_logged,
        avg_meals_per_day=round(len(meals) / days_logged, 1) if days_logged else 0,
        by_meal_type=by_meal_type,
        top_foods=top_foods,
        breakfast_skipped_days=sum(1 for types in meal_types_by_day.values() if "breakfast" not in types),
        weekday_avg_calories=round(sum(weekday) / len(weekday), 1) if weekday else None,
        weekend_avg_calories=round(sum(weekend) / len(weekend), 1) if weekend else None,
    )