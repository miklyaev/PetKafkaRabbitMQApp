# Скрипт для автоматического коммита и пуша на GitHub (Windows / PowerShell)

Write-Host "Добавление файлов в индекс..." -ForegroundColor Cyan
git add .

# Получаем статус изменений
$status = git status --short

if ([string]::IsNullOrWhiteSpace($status)) {
    Write-Host "Нет изменений для коммита." -ForegroundColor Yellow
    exit 0
}

# Генерация комментария
$date = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$commitMessage = "Автоматический коммит: $date`n`nИзмененные файлы:`n$status"

Write-Host "Создание коммита..." -ForegroundColor Cyan
git commit -m $commitMessage

Write-Host "Отправка на GitHub..." -ForegroundColor Cyan
# При необходимости здесь можно поменять 'master' на 'main', если ветка по умолчанию называется main
git push origin master

Write-Host "Успешно завершено!" -ForegroundColor Green
