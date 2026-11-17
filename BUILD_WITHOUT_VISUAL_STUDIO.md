# Получить EXE без установки Visual Studio

В архив добавлен GitHub Actions workflow для сборки Windows x64.

1. Создай бесплатный репозиторий на GitHub.
2. Загрузи туда всё содержимое этого проекта, включая скрытую папку `.github`.
3. Открой вкладку Actions.
4. Выбери workflow **Build Windows app** и нажми **Run workflow**.
5. После успешной сборки открой запуск workflow и скачай artifact `GeminiNetworkAssistant-win-x64`.
6. Распакуй artifact и запусти `GeminiNetworkAssistant.exe`.

Сборка self-contained: .NET Runtime отдельно устанавливать не требуется.
Visual Studio на компьютере не нужна. Нужен доступ к GitHub для облачной сборки.

Примечание: этот workflow пока публикует x64 Windows-приложение. Он не превращает программу в установщик.
