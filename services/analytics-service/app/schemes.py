from datetime import date, datetime
from typing import Literal

from pydantic import BaseModel, Field


class MealIn(BaseModel):
    user_id: str = Field(min_length=1, max_length=64, examples=["user-1"])
    eaten_at: datetime = Field(examples=["2026-11-05T08:30:00Z"])
    meal_type: Literal["breakfast", "lunch", "dinner", "snack", "other"] = "other"
    name: str = Field(max_length=200, examples=["Вівсянка з бананом"])
    calories: float = Field(ge=0, le=10000, examples=[420])
    protein: float = Field(default=0, ge=0, le=1000, examples=[14])
    fat: float = Field(default=0, ge=0, le=1000, examples=[9])
    carbs: float = Field(default=0, ge=0, le=1000, examples=[70])

class DayStats(BaseModel):
    day: date
    calories: float
    protein: float
    fat: float
    carbs: float
    meals_count: int

class RangeStats(BaseModel):
    date_from: date
    date_to: date
    days_logged: int

    total_calories: float
    total_protein: float
    total_fat: float
    total_carbs: float

    avg_calories_per_day: float

    protein_percent: float
    fat_percent: float
    carbs_percent: float

    days: list[DayStats]

class GoalIn(BaseModel):
    daily_calories: float = Field(gt=0, le=10000, examples=[2000])
    valid_from: date | None = None


class GoalOut(BaseModel):
    daily_calories: float
    valid_from: date

class ProgressReport(BaseModel):
    date_from: date
    date_to: date
    current_goal: float | None

    days_logged: int
    days_with_goal: int
    days_on_target: int
    days_over: int
    days_under: int

    on_target_percent: float | None
    avg_calories: float | None
    avg_deviation_percent: float | None
    current_streak_days: int

class MealTypeHabit(BaseModel):
    meal_type: str
    count: int
    avg_calories: float
    share_of_calories_percent: float


class TopFood(BaseModel):
    name: str
    count: int
    total_calories: float


class HabitsReport(BaseModel):
    date_from: date
    date_to: date
    meals_logged: int
    days_logged: int
    avg_meals_per_day: float
    by_meal_type: list[MealTypeHabit]
    top_foods: list[TopFood]
    breakfast_skipped_days: int
    weekday_avg_calories: float | None
    weekend_avg_calories: float | None