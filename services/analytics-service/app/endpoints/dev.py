from fastapi import APIRouter, Depends
from sqlalchemy.dialects.postgresql import insert
from sqlalchemy.ext.asyncio import AsyncSession

from app.db import get_session
from app.tables import DailyStat, MealEntry
from app.schemes import MealIn

router = APIRouter(prefix="/analytics/dev", tags=["Dev: тестові дані"])


@router.post("/meals", status_code=201)
async def add_meal(meal: MealIn, session: AsyncSession = Depends(get_session)):
    day = meal.eaten_at.date()

    session.add(MealEntry(
        user_id=meal.user_id,
        day=day,
        eaten_at=meal.eaten_at,
        meal_type=meal.meal_type,
        name=meal.name,
        calories=meal.calories,
        protein=meal.protein,
        fat=meal.fat,
        carbs=meal.carbs,
    ))

    stmt = insert(DailyStat).values(
        user_id=meal.user_id,
        day=day,
        calories=meal.calories,
        protein=meal.protein,
        fat=meal.fat,
        carbs=meal.carbs,
        meals_count=1,
    )
    stmt = stmt.on_conflict_do_update(
        index_elements=["user_id", "day"],
        set_={
            "calories": DailyStat.calories + stmt.excluded.calories,
            "protein": DailyStat.protein + stmt.excluded.protein,
            "fat": DailyStat.fat + stmt.excluded.fat,
            "carbs": DailyStat.carbs + stmt.excluded.carbs,
            "meals_count": DailyStat.meals_count + 1,
        },
    )
    await session.execute(stmt)
    await session.commit()

    return {"status": "saved", "day": day}