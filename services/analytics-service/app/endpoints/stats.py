from datetime import date, timedelta

from fastapi import APIRouter, Depends, HTTPException, Query
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.db import get_session
from app.schemes import DayStats, RangeStats
from app.security import get_current_user_id
from app.tables import DailyStat

router = APIRouter(prefix="/analytics/stats", tags=["Статистика"])


def to_day_stats(day: date, row: DailyStat | None) -> DayStats:
    if row is None:
        return DayStats(day=day, calories=0, protein=0, fat=0, carbs=0, meals_count=0)
    return DayStats(
        day=day,
        calories=row.calories,
        protein=row.protein,
        fat=row.fat,
        carbs=row.carbs,
        meals_count=row.meals_count,
    )


@router.get("/daily", response_model=DayStats)
async def get_daily(
    user_id: str = Depends(get_current_user_id),
    day: date | None = Query(None),
    session: AsyncSession = Depends(get_session),
):
    day = day or date.today()
    row = await session.get(DailyStat, (user_id, day))
    return to_day_stats(day, row)


@router.get("/range", response_model=RangeStats)
async def get_range(
    user_id: str = Depends(get_current_user_id),
    date_from: date | None = Query(None),
    date_to: date | None = Query(None),
    session: AsyncSession = Depends(get_session),
):
    date_to = date_to or date.today()
    date_from = date_from or date_to - timedelta(days=6)

    if date_from > date_to:
        raise HTTPException(422, "date_from не може бути пізніше за date_to")
    if (date_to - date_from).days > 365:
        raise HTTPException(422, "Період не може бути довшим за рік")

    result = await session.execute(
        select(DailyStat).where(
            DailyStat.user_id == user_id,
            DailyStat.day.between(date_from, date_to),
        )
    )
    rows = {row.day: row for row in result.scalars().all()}

    total_calories = sum(r.calories for r in rows.values())
    total_protein = sum(r.protein for r in rows.values())
    total_fat = sum(r.fat for r in rows.values())
    total_carbs = sum(r.carbs for r in rows.values())

    days_logged = len(rows)
    avg_calories = total_calories / days_logged if days_logged else 0

    p, f, c = total_protein * 4, total_fat * 9, total_carbs * 4
    macro_total = p + f + c

    def percent(x: float) -> float:
        return round(x / macro_total * 100, 1) if macro_total else 0

    all_days = [date_from + timedelta(days=i) for i in range((date_to - date_from).days + 1)]

    return RangeStats(
        date_from=date_from,
        date_to=date_to,
        days_logged=days_logged,
        total_calories=round(total_calories, 1),
        total_protein=round(total_protein, 1),
        total_fat=round(total_fat, 1),
        total_carbs=round(total_carbs, 1),
        avg_calories_per_day=round(avg_calories, 1),
        protein_percent=percent(p),
        fat_percent=percent(f),
        carbs_percent=percent(c),
        days=[to_day_stats(d, rows.get(d)) for d in all_days],
    )