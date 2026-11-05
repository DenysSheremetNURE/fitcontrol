"""Builds foods.tsv from the USDA SR Legacy CSV download.

Usage: python build_foods_seed.py <extracted SR Legacy folder> <output foods.tsv>
Source: https://fdc.nal.usda.gov/download-datasets/ (FoodData_Central_sr_legacy_food_csv_2018-04.zip)
"""
import csv
import os
import sys
from decimal import ROUND_HALF_UP, Decimal

ENERGY_KCAL, PROTEIN, FAT, CARBS = "1008", "1003", "1004", "1005"
NUTRIENTS = (ENERGY_KCAL, PROTEIN, FAT, CARBS)
COLUMNS = ("id", "name", "category", "kcal", "protein", "fat", "carbs")


def rows(folder, name):
    with open(os.path.join(folder, name), encoding="utf-8-sig", newline="") as f:
        yield from csv.DictReader(f)


def main(folder, output):
    categories = {r["id"]: r["description"] for r in rows(folder, "food_category.csv")}
    foods = {r["fdc_id"]: r for r in rows(folder, "food.csv")}

    amounts = {}
    for r in rows(folder, "food_nutrient.csv"):
        if r["nutrient_id"] in NUTRIENTS and r["fdc_id"] in foods:
            value = Decimal(r["amount"]).quantize(Decimal("0.01"), ROUND_HALF_UP)
            amounts.setdefault(r["fdc_id"], {})[r["nutrient_id"]] = value

    written = 0
    with open(output, "w", encoding="utf-8", newline="\n") as f:
        f.write("\t".join(COLUMNS) + "\n")
        for fdc_id in sorted(foods, key=int):
            food = foods[fdc_id]
            values = amounts[fdc_id]
            cells = [
                fdc_id,
                food["description"],
                categories[food["food_category_id"]],
                *(str(values[n]) for n in NUTRIENTS),
            ]
            f.write("\t".join(cells) + "\n")
            written += 1
    print(f"written {written} foods to {output}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
