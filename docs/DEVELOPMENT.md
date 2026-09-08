# Сборка и тестирование

Нужны Windows 11 x64, .NET SDK 10 и PowerShell. Интерфейс написан на WPF, логика приложения на C#.

## Собрать приложение

Из корня репозитория:

```powershell
# Build first; the host disk is not a test fixture.
./scripts/publish.ps1
```

Готовые `JunkManager.exe` и `JunkManager.Cli.exe` появятся в `artifacts/publish`. Оба файла содержат .NET и встроенный каталог правил. Сам скрипт сборки не запускает приложение и тесты.

## Проверить изменения

Используй отдельную Windows VM со снимком состояния. Все запуски приложения и тестов при разработке выполняются внутри неё. Перед разрушительными проверками сохрани снимок, после них откати VM.

Тестовый пакет можно собрать на хосте:

```powershell
# Ship the test bench to the VM, not the other way around.
dotnet publish tests/JunkManager.Tests -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts/tests
```

Скопируй весь каталог `artifacts/tests` в VM. В гостевом PowerShell с правами администратора создай маркер и запусти выбранный класс из каталога тестового пакета:

```powershell
# These commands belong inside the disposable VM. The main disk has other plans.
$env:JUNKMANAGER_TEST_VM = '1'
New-Item -ItemType File -Path 'C:\junkmanager-test-vm.marker' -Force
./JunkManager.Tests.exe -trait Class=Sandbox -result-xml sandbox.xml
./JunkManager.Tests.exe -trait Class=Ui -result-xml ui.xml
```

Для UI-тестов нужен открытый рабочий стол VM без блокировки. Самостоятельно создавать маркер на основном компьютере нельзя: он разрешает разрушительные проверки, а не превращает компьютер в песочницу.

| Класс | Что проверяет |
| --- | --- |
| Sandbox | Логику на временных файлах и собственных записях реестра |
| Ui | Настоящее окно приложения через UI Automation |
| LiveRead | Чтение состояния гостевой Windows |
| LiveDestructive | Удаление в настоящих системных источниках гостя |
| Seeded | Обнаружение подготовленных находок и сохранность контрольных файлов |

Начинай с Sandbox и Ui. Классы LiveDestructive и Seeded требуют отдельной подготовки гостя и снимка состояния. Тесты с пометкой Explicit не запускаются автоматически: их предусловия указаны в исходниках.

## Структура

- `src/JunkManager.App`: WPF-интерфейс и модели экранов.
- `src/JunkManager.Core`: поиск файлов, инвентарь программ и проверка реестра.
- `src/JunkManager.Safety`: ограничения путей, прав и тестовый предохранитель.
- `src/JunkManager.Deletion`: удаление и журнал операций.
- `src/JunkManager.Cli`: команды без графического интерфейса.
- `rules`: каталог, который встраивается при сборке.
- `tests`: автоматические проверки.

GitHub Actions собирает проект и сохраняет артефакт. Этот процесс не заменяет проверку приложения в VM. Исполняемые файлы и результаты тестов в Git не коммитятся.
