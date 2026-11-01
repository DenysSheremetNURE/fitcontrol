from fastapi import FastAPI

app = FastAPI(
    title="FitControl — Analytics & Reporting Service",
    docs_url="/analytics/docs",
    openapi_url="/analytics/openapi.json",
)


@app.get("/health")
async def health():
    return {"status": "ok"}