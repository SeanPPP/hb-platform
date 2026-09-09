FROM python:3.11-slim
WORKDIR /app
COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt
COPY models.py download_models.py ./
RUN python download_models.py --directory /app/.models
COPY app.py recognizer.py ./
COPY licenses ./licenses
RUN useradd --system --uid 10001 face
USER 10001
EXPOSE 8080
CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "8080", "--no-access-log", "--workers", "1", "--limit-concurrency", "4"]
