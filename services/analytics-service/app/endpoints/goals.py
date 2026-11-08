from datetime import date

from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.db import get_session
from app.schemes import GoalIn, GoalOut
from app.security import get_current_user_id
from app.tables import GoalHistory

router = APIRouter(prefix="/analytics/goals", tags=["Цілі"])


@router.put("/me", response_model=GoalOut)
async def set_goal(
    goal: GoalIn,
    user_id: str = Depends(get_current_user_id),
    session: AsyncSession = Depends(get_session),
):
    row = GoalHistory(
        user_id=user_id,
        valid_from=goal.valid_from or date.today(),
        daily_calories=goal.daily_calories,
    )
    session.add(row)
    await session.commit()
    return GoalOut(daily_calories=row.daily_calories, valid_from=row.valid_from)


@router.get("/me", response_model=GoalOut)
async def get_goal(
    user_id: str = Depends(get_current_user_id),
    session: AsyncSession = Depends(get_session),
):
    result = await session.execute(
        select(GoalHistory)
        .where(GoalHistory.user_id == user_id, GoalHistory.valid_from <= date.today())
        .order_by(GoalHistory.valid_from.desc(), GoalHistory.id.desc())
        .limit(1)
    )
    row = result.scalar_one_or_none()
    if row is None:
        raise HTTPException(404, "Ціль ще не встановлена")
    return GoalOut(daily_calories=row.daily_calories, valid_from=row.valid_from)