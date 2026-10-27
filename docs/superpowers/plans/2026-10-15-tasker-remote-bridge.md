# Tasker Remote Bridge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Полное управление Tasker серверного Cronopus из локального Cronopus по IP, включая настройки, payload, расписания и загрузку файлов/папок.

**Architecture:** Отдельный HTTPS-хост в процессе сервера вызывает общий сервис Tasker. Локальная панель использует adapter local/remote; соединение с сервером и секреты принадлежат локальному backend. Один существующий SchedulerService продолжает выполнять задачи независимо от клиента.

**Tech Stack:** .NET 10, Kestrel, HttpClient, System.Text.Json, SQLite/PostgreSQL, DPAPI на Windows, существующие HTML/JS/CSS, node:test и отдельный C# integration runner.

**Spec:** [Согласованная спецификация](../specs/2026-10-15-tasker-remote-bridge-design.md).

## Global Constraints

- «Проект собирается для `net10.0-windows` и `net10.0`; оба таргета должны сохраняться.»
- «Новый порт по умолчанию — 38473, настраиваемый, независимо от панели и replay.»
- «Если bind не удался, попробовать порт 0; показать фактический адрес и причину fallback.»
- «Существующие правила портов панели и replay в этой задаче не изменяются.»
- «Эти токены сохраняют своё назначение и не дают доступ к новому удалённому входу.»
- «На сервере создавать именованные ключи доступа из 32 криптографически случайных байт.»
- «JSON тела до 8 MiB, обычный таймаут 15 секунд; долгие операции используют operation_id.»
- «Произвольное удаление каталогов и удалённый файловый менеджер общего назначения не входят в объём.»
- «Не дублировать планировщик, правила payload, расписаний или запуска процессов.»
- Все shell-команды с `rtk`; новые документы создавать/дополнять порциями до 50 строк.
- Перед UI-кодом прочитать `C:/Users/l3gi0n/.codex/skills/ui-themes/SKILL.md`; сохранять активную тему.
- Работать только с временными тестовыми БД/каталогами; не запускать пользовательские задачи.
- Отдельные изменения общего Db, GeeTest, ZP transport и выпуска версии не нужны.
- Эти этапы зависимы и образуют одну поставку; частично сделанный мост не считается готовой фичей.

## Review Focus

- Большой stdout без переводов строк или разорванный UTF-8: ограничение байт и целые символы (задача 10).
- Диски без прав, UNC и junction, подменённый во время commit: явная ошибка без записи вне назначения (8–9).
- Вторая локальная панель и прямое редактирование задачи на сервере: сохранение через тот же CAS (2, 7).
- Смена БД сервера при живом клиенте: сброс курсоров и отклонение старого черновика (2, 5, 7).
- Рестарт/смена IP/сертификата при незавершённой загрузке: нет тихого переподключения к другому узлу (3–4, 9).

## Структура и правила проверки

Новые runtime-файлы — `Controllers/Bridge/`; HTTP-адаптеры — `Handlers/Bridge*.cs`.
Общий сервис — `Controllers/Tasker/`, существующий SchedulerHandler становится его адаптером.
Общая схема меняется только в таблице schedules; секреты и операции моста живут вне БД задач.
Тесты C# — отдельный `tests/Bridge.Integration/`, без добавления нового test framework.
Каждый этап: сначала отрицательный поведенческий тест, запуск с FAIL, затем реализация и PASS.
Изменённые файлы коммитить явным списком; не использовать `git add .` и не пушить автоматически.

## Задача 1. Общие операции Tasker и независимый стенд

**Files:** создать `Controllers/Tasker/TaskerContracts.cs`, `TaskerService.cs`;
изменить `Handlers/SchedulerHandler.cs`, `Program.cs`; создать
`tests/Bridge.Integration/Bridge.Integration.csproj`, `Program.cs`, `BridgeFixture.cs`, `TaskerCases.cs`.
**Interfaces:** `TaskerRequest(TaskerOperation Operation, string? TaskId, string? ExpectedRevision, JsonElement Body)`;
`TaskerReply(JsonElement Data, string? Revision)`; `Task<TaskerReply> TaskerService.InvokeAsync(TaskerRequest request, CancellationToken ct)`.
Enum содержит List, Get, Create, Save, Clone, Delete, Run, Stop, Pause, Resume,
Defer, Enable, Disable, ClearQueue, KillInstance, ResetSuccesses, SavePayload,
GetPayload, Instances, Queue, Resources, ScriptParams, ArgsPreview, SchedulePreview,
ScanFolder, PackageScripts, GetConfig, SaveConfig, Build, EnsureVenv, Install.

- [ ] Перенести в тест TaskerCases сценарий создания Python-задачи с `schedule_mode=off`,
  временным script_path, schema/values; вызвать сервис и прочитать сохранённую запись.
  Fixture предоставляет `Service`, `CreatePythonAsync()`, `GetAsync(id)` и `TempDirectory`;
  использует отдельную SQLite БД и DbConnectionService, не вызывает Program приложения.
  C# runner: `Check(bool ok, string message)` бросает исключение; `--case <name>` выбирает тест.
  Проект: net10.0-windows, RuntimeIdentifier=win-x64, SelfContained=true,
  ProjectReference на ../../Cronopus.csproj; основной проект уже исключает tests/**/*.cs.
```csharp
var saved = await f.CreatePythonAsync();
var read = await f.GetAsync(saved.Data.GetProperty("id").GetString()!);
Check(read.Data.GetProperty("payload_values").GetString() == "{\"count\":2}", "payload lost");
```
- [ ] Запустить `rtk dotnet run --project tests/Bridge.Integration/Bridge.Integration.csproj -- --case tasker-save`;
  первый запуск должен FAIL из-за отсутствующего сервиса, не из-за посторонней ошибки сборки.
- [ ] Выделить затрагиваемые операции из SchedulerHandler; тело запроса проверять
  по whitelist полей, не разрешать изменение status/last_output. ID параметризовать
  в отдельном repository запросе либо строго нормализовать/экранировать во всех SQL.
  Обе HTTP-поверхности используют один сервис; локальные диалоги остаются в handler.
  Сохранить подписи старых /tasker маршрутов, типы строковых полей и EvaluateNow после save.
  Перенести все перечисленные enum операции, а не только создание/запуск.
- [ ] Выполнить тест, затем `rtk proxy node --test tests/tasker.*.test.js`;
  добавить реальные проверки clone, payload, run count/args, stop и queue в TaskerCases.
- [ ] Коммит `refactor: share Tasker operations between local and remote adapters`.

## Задача 2. Версии конфигурации и конфликтующие изменения

**Files:** изменить `Sql/DbSchema.cs`, `Controllers/Tasker/TaskerService.cs`;
создать `Controllers/Tasker/TaskerRepository.cs`, `tests/Bridge.Integration/RevisionCases.cs`.
**Interfaces:** revision = `<database-epoch>:<edit-version>`;
`TaskerRepository.SaveAsync(TaskerRequest request, CancellationToken ct)` возвращает TaskerReply.

- [ ] Добавить тесты stale revision, параллельного PUT, локального save во время
  удалённого редактирования и смены БД через существующее событие
  `DbConnectionService.Connected`; новое событие не добавлять.
```csharp
var a = await f.CreatePythonAsync();
await f.SaveNameAsync(a, "first");
await f.ExpectConflictAsync(() => f.SaveNameAsync(a, "second"));
Check((await f.GetAsync(a.Data.GetProperty("id").GetString()!)).Data.GetProperty("name").GetString() == "first", "lost update");
```
  Fixture определить `SaveNameAsync(TaskerReply,string)` и `ExpectConflictAsync(Func<Task>)`.
- [ ] Запустить runner `--case revision`; убедиться, что потеря обновления обнаруживается.
- [ ] Добавить schedules.edit_version через существующий PrepareTable: начальное TEXT '1'.
  Настройки, путь и payload писать одной SQL-операцией с условием id/edit_version,
  новым случайным version и `RETURNING`; отсутствие строки трактовать как конфликт.
  Отдельное сохранение payload использует тот же CAS; счётчики не увеличивают edit_version.
  Базовая форма параметризованного UPDATE для полного save (добавить остальные
  разрешённые настройки в тот же statement, не отдельными запросами):
```sql
UPDATE "schedules"
SET "name" = @name, "script_path" = @path,
    "payload_schema" = @schema, "payload_values" = @values,
    "edit_version" = @next
WHERE "id" = @id AND "edit_version" = @expected
RETURNING "edit_version";
```
  Старые локальные команды без revision сериализовать через сервис, увеличивать version.
  На смене подключённой БД менять epoch и возвращать database_changed для старого черновика.
- [ ] Проверить SQLite; повторить CAS на disposable PostgreSQL через переменную
  `BRIDGE_TEST_PG`, если сервер доступен. Отсутствие PostgreSQL записать как непроверенное.
- [ ] Коммит `feat: protect Tasker configuration from conflicting saves`.

## Задача 3. Локальное хранилище, сертификат и ключи

**Files:** создать `Controllers/Bridge/BridgeContracts.cs`, `BridgeStore.cs`,
`BridgeSecrets.cs`, `BridgeIdentity.cs`, `BridgeKeyRegistry.cs`, `tests/Bridge.Integration/IdentityCases.cs`;
изменить `Cronopus.csproj` только при необходимости ссылки на DPAPI.
**Interfaces:** `BridgeConnection(Id, Name, Address, NodeId, Fingerprint, HasKey)`;
`BridgeStore(root)`; `BridgeIdentity.LoadOrCreateAsync(IPAddress address, CancellationToken ct)`;
`BridgeKeyRegistry.Create(name)` → `(keyId, secret)`; `Authorize(secret)` → keyId/null;
`Revoke(keyId)`; `GetRevocationToken(keyId)` → CancellationToken.

- [ ] Тест: перезапуск сохраняет node_id/cert; один ключ не авторизует другой;
  отзыв отменяет token; сериализованный публичный connection не содержит секрета.
```csharp
var keys = new BridgeKeyRegistry(f.TempDirectory);
var issued = keys.Create("desktop");
Check(keys.Authorize(issued.secret) == issued.keyId, "key rejected");
keys.Revoke(issued.keyId);
Check(keys.Authorize(issued.secret) is null, "revoked key accepted");
```
- [ ] Запустить runner `--case identity`, получить FAIL, затем реализовать store
  в LocalApplicationData/Cronopus/bridge; запись temp+rename.
  Для нескольких независимых установок предусмотреть `CRONOPUS_BRIDGE_STATE_DIR`:
  только абсолютный root с теми же ACL/защитой. Fixture задаёт его дочерним процессам
  в temp; в UI показывать фактическое расположение store, без содержимого секретов.
  Сертификат на 2 года с IP SAN; SHA-256 pin от DER сертификата; random key 32 байта,
  SHA-256 в registry, fixed-time compare. DPAPI CurrentUser+ACL на Windows;
  private mode 0600/0700 вне Windows; публичные DTO не содержат private properties.
- [ ] PASS: прочитать файлы store и убедиться, что plaintext secret отсутствует на Windows;
  сохранить старую конфигурацию при ошибке записи, expiry показать в статусе.
- [ ] Коммит `feat: persist bridge identity and revocable access keys`.

## Задача 4. HTTPS listener и клиент с закреплением сертификата

**Files:** создать `Controllers/Bridge/BridgeHost.cs`, `BridgeClient.cs`, `BridgeAuth.cs`,
`BridgeAudit.cs`, `tests/Bridge.Integration/TlsCases.cs`; изменить `Cronopus.csproj`, `Program.cs`.
**Interfaces:** `BridgeHost.StartAsync(CancellationToken ct)` → фактический Uri;
`BridgeHost.StopAsync(ct)`; `BridgeClient.SendAsync(connectionId, HttpMethod method,
string relativePath, HttpContent? content, CancellationToken ct)` → HttpResponseMessage.
Client использует только адрес из BridgeStore; relativePath начинается /bridge/v1/.

- [ ] Тесты запуска/остановки, отсутствующего/отозванного Bearer, неверного pin,
  просроченного сертификата, занятого порта, выключенного listener и чужого node_id.
  Fixture получает `StartHttpsAsync()`, `SendAsync(method,path,body)`, `Client`, `BaseUri`.
```csharp
await f.StartHttpsAsync();
using var denied = await f.AnonymousClient.GetAsync(new Uri(f.BaseUri, "/bridge/v1/info"));
Check((int)denied.StatusCode == 401, "unauthenticated bridge");
using var allowed = await f.SendAsync("GET", "/bridge/v1/info", null);
Check(allowed.IsSuccessStatusCode, "valid key denied");
```
- [ ] Запустить runner `--case tls`; реализовать FrameworkReference Microsoft.AspNetCore.App,
  Kestrel HTTPS на заданном IP/порту, после bind failure повторить на 0 новым host instance.
  Адрес публикации отделить от bind address; печатать фактический HTTPS URL.
  Авторизацию выполнять до разбора body; TLS pin/срок/имя или IP SAN проверять в
  callback конкретного HttpClientHandler; не следовать redirect, не доверять system proxy.
  Клиентские потоки обрабатывать с ResponseHeadersRead и отдельным timeout ожидания
  заголовков; 15 секунд не должны обрывать исправный долгий поток или загрузку.
  Вызов /info сверяет сохранённый node_id; несовпадение блокирует дальнейшие команды.
- [ ] Добавить per-key SemaphoreSlim(8), JSON 8 MiB, 15s timeout, 429 Retry-After;
  отмену при отзыве связывать с request cancellation. Ключи не писать в audit;
  ротация 5×10 MiB, методы API только из whitelist, остальные возвращают 404/405.
  Не менять HttpListener префиксы панели. Изолированный store должен работать без БД задач.
- [ ] PASS: два клиента с разными pins не меняют доверие друг друга; остановка
  освобождает порт; listener failure не останавливает локальную панель/планировщик.
- [ ] Коммит `feat: host authenticated HTTPS bridge with pinned certificates`.

## Задача 5. API задач, страницы и журнал команд

**Files:** создать `Handlers/BridgeApi.cs`, `Controllers/Bridge/BridgeOperations.cs`,
`BridgeChanges.cs`, `BridgePaging.cs`, `tests/Bridge.Integration/ApiCases.cs`;
изменить `Controllers/Tasker/TaskerService.cs`, `Program.cs`.
**Interfaces:** `BridgeOperations.ExecuteAsync(keyId, requestId, bodyHash, Func<Task<TaskerReply>> action, ct)`;
`Get(keyId, requestId)` → JSON состояния pending/succeeded/failed/outcome_unknown;
`BridgeChanges.Read(cursor, limit)` → JSON с next_cursor и reset_required.

- [ ] ApiCases: Create/Get/Save/Clone/Delete с revision; все actions из спецификации;
  повтор одного request_id, другой body с тем же ID, потеря ответа после run,
  рестарт с pending, лимит страницы и смена БД. Fixture: `RunOnceAsync(id,requestId)`
  возвращает operation JSON, `CountQueuedAsync(id)` считает очередь непосредственно в test DB.
```csharp
var task = await f.CreatePythonAsync();
var id = task.Data.GetProperty("id").GetString()!;
await f.RunOnceAsync(id, "same-request");
await f.RunOnceAsync(id, "same-request");
Check(await f.CountQueuedAsync(id) == 1, "duplicate run");
```
- [ ] Запустить runner `--case api`; перед тестом очереди не запускать scheduler tick.
  Реализовать все маршруты spec §5, mapping только к TaskerOperation, нормализованный JSON hash.
  Журнал операций: отдельная локальная SQLite, unique(key_id, request_id), durable pending
  до side effect; сериализация конкурентного claim, 7 суток хранения, максимум 100000
  записей; при заполнении не вытеснять живые pending, отвечать 503 до освобождения места.
  После аварии pending пометить outcome_unknown; не повторять мутации за пользователя.
- [ ] Страницы: SQL LIMIT с фильтром/стабильным ключом sort+id, не получать полную БД.
  Разрешённые sort поля whitelist; курсор содержит фильтр, сортировку и database epoch.
  /changes: bounded ring 10000 событий; expiry/restart/DB switch требуют reset.
  Публиковать изменения конфигурации и переходы scheduler состояния через BridgeChanges;
  статистику агрегировать одним sampler раз в 3 секунды, без обхода полных логов.
  События sampler не считаются командой и не меняют edit_version.
- [ ] Долгие build/ensure-venv/install выполнять как операции с bounded progress 256 KiB;
  `GET /operations/{request_id}` возвращает operation_id и прогресс по курсору.
  /info возвращает capabilities, лимиты, timezone/time, версии и database epoch.
  Разбор Python параметров/config/preview использует именно серверное окружение.
- [ ] PASS всех routes; no output/schema/values в /tasks; обычные ошибки по spec §5.
  Проверить данные с кавычками, Unicode и ID другого узла; audit не содержит body.
- [ ] Коммит `feat: expose paginated Tasker API and durable command outcomes`.

## Задача 6. Локальный backend подключений и настройки

**Files:** создать `Handlers/BridgeLocalHandler.cs`, `wwwroot/js/bridge-config.js`;
изменить `Program.cs`, `wwwroot/config.html`, `wwwroot/css/config.css`;
создать `tests/bridge.config.test.js`, `tests/Bridge.Integration/LocalProxyCases.cs`.
**Interfaces:** loopback `/bridge-local/connections` GET/POST, `/{id}` PUT/DELETE,
`/{id}/test` POST, `/server` GET/PUT, `/keys` POST, `/keys/{id}` DELETE;
`/{id}/api/<allowed-path>` проксирует только определённый /bridge/v1 маршрут и verb.
Секрет обновляется только при переданном новом значении; пустое поле не удаляет ключ.

- [ ] Node test с fake fetch: сохранение connection не возвращает key, reload
  показывает HasKey, временная 503 не очищает ключ, тест несовпадения node_id виден.
```javascript
const { createConfigController } = require('../wwwroot/js/bridge-config.js');
const calls = [];
const controller = createConfigController({ send: async (...a) => { calls.push(a); return { hasKey: true }; } });
await controller.save({ id: 'srv', name: 'Server', key: '' });
assert.equal(Object.hasOwn(calls[0][2], 'key'), false);
```
- [ ] `rtk proxy node --test tests/bridge.config.test.js` → FAIL.
- [ ] Прочитать ui-themes и реализовать Config → Remote Tasker: список подключений,
  адрес, pin, node_id, одноразовый ввод ключа, проверка/удаление; сервер enable,
  bind/advertised IP, порт, срок cert, создание/отзыв ключей и инструкция firewall.
  Добавить секцию в существующую cfg-rail, не менять другие секции и тему.
  `createConfigController({send})` экспортировать для node через условный module.exports.
- [ ] LocalProxyCases: нельзя отправить absolute URL/redirect/../encoded traversal;
  запрос другого Origin или без `X-Bridge-Local: 1` отклоняется до выполнения.
  Зафиксировать same-origin Origin/Host и проверку custom header на всех local bridge
  маршрутах, включая чтение; не наследовать разрешающий CORS для этих маршрутов.
  В JS send использует header; streaming идет через fetch, не через EventSource без header.
  /server и /keys нельзя проксировать на другой узел; их администрирует только его локальная панель.
- [ ] PASS Node и C# `--case local-proxy`; в браузерных ответах/консоли ключа нет,
  сохранение секретов переживает рестарт и не попадает в appsettings/export.
- [ ] Коммит `feat: configure bridge servers and client connections locally`.

## Задача 7. Выбор узла и все формы Tasker

**Files:** создать `wwwroot/js/tasker-transport.js`, `tests/tasker.transport.test.js`;
изменить `wwwroot/tasker.html`, `wwwroot/js/tasker.js`, `wwwroot/css/tasker.css`,
`Handlers/BridgeLocalHandler.cs`.
**Interfaces:** `createTaskerTransport({send})` → объект с `selectNode(connectionId|null)`,
`request(operation, {taskId, body, revision, signal})`, `isCurrent(context)`, `context()`;
контекст `{nodeId, taskId, generation}`. `send` локально обращается к старым /tasker
маршрутам, удалённо — к разрешённому /bridge-local/{connectionId}/api маршруту.

- [ ] Тест fake delayed response: узел A и B имеют одинаковый taskId; ответ A
  после переключения не меняет B. Контекст фиксируется до начала запроса.
```javascript
const t = createTaskerTransport({ send: async () => ({}) });
t.selectNode('A'); const old = t.context();
t.selectNode('B');
assert.equal(t.isCurrent(old), false);
```
- [ ] Запустить `rtk proxy node --test tests/tasker.transport.test.js` → FAIL.
- [ ] Добавить node selector и состояние связи; заменить все обращения Tasker,
  включая config/payload/build/install/preview/queue/resources, единым adapter.
  Получать полный detail при выборе задачи; список использует страницы/changes.
  Поллинг останавливать в hidden; минимум 3 секунды, один in-flight request.
  При переключении abort requests, timers и streams; сбросить selection/data,
  проверить сохранение/отбрасывание/отмену dirty draft до смены node.
  Cache и last-folder индексируются connectionId; offline не вызывает local fallback.
  Сохранять revision формы; 409 оставляет draft и предлагает перечитать серверную версию.
  Clock/timezone показывать от сервера; database_changed предлагает перечитать задачу.
- [ ] Локальные open-terminal/editor/OS-folder скрыть в remote; picker заменить
  браузером файлов задачи 8. Отсутствующие capabilities явно блокируют действие.
  Кнопки run не имеют автоматического retry; потеря ответа использует operation polling.
- [ ] PASS: dirty switch, 503, 401, смена БД, clone/settings navigation,
  смена скрипта/From Script, payload Save, расписание и late async callbacks.
  Выполнить `rtk proxy node --test tests/tasker.*.test.js`, ручной браузерный прогон двух узлов.
- [ ] Коммит `feat: control a selected remote node from the Tasker interface`.

## Задача 8. Просмотр серверных дисков и выбор скрипта

**Files:** создать `Controllers/Bridge/BridgeFiles.cs`, `Handlers/BridgeFilesApi.cs`,
`wwwroot/js/tasker-files.js`, `tests/tasker.files.test.js`,
`tests/Bridge.Integration/FileCases.cs`; изменить `BridgeApi.cs`, `tasker.html`, `tasker.css`.
**Interfaces:** `/files/roots`, `/files/list?path&cursor`, `POST /files/directories`;
`BridgeFiles.ListAsync(path,cursor,limit,ct)` и `CreateDirectoryAsync(path,ct)` → JsonElement.
`createFileBrowser({send,connectionId})` предоставляет `open(path)` и `select(path)`.

- [ ] FileCases: диск/родитель, пагинация 100/500, Unicode, каталог без прав,
  UNC без доступа и скрытые/system элементы в пределах прав ОС; нет рекурсивного обхода.
```csharp
Directory.CreateDirectory(Path.Combine(f.TempDirectory, "скрипты"));
var files = new BridgeFiles();
var page = await files.ListAsync(f.TempDirectory, null, 100, CancellationToken.None);
Check(page.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("name").GetString() == "скрипты"), "Unicode directory missing");
```
- [ ] Runner `--case files` и `rtk proxy node --test tests/tasker.files.test.js` → FAIL.
- [ ] Реализовать listing с лимитом и стабильным курсором; ошибку доступа вернуть
  на конкретном каталоге. Путь выбирается на сервере и возвращается без локальной нормализации.
  Навигацию по явному пути поддержать, рекурсивное разворачивание ссылок не выполнять.
  UI: диски, breadcrumbs, ввод пути, создание папки, выбор файла/каталога и last-folder.
- [ ] PASS: выбор пути меняет taskDraft выбранного узла, вызывает server ScriptParams,
  сохраняет прочие поля и защищает от старого ответа после смены узла.
- [ ] Коммит `feat: browse server disks and select remote task scripts`.

## Задача 9. Возобновляемая серверная передача файлов

**Files:** создать `Controllers/Bridge/BridgeUploads.cs`, `BridgeUploadPaths.cs`,
`Handlers/BridgeUploadsApi.cs`, `tests/Bridge.Integration/UploadCases.cs`;
изменить `BridgeApi.cs`, `BridgeLocalHandler.cs`.
**Interfaces:** `POST /uploads`, `GET /uploads/{id}`, `DELETE /uploads/{id}`,
`PUT /uploads/{id}/files/{file_id}?offset`, `POST /uploads/{id}/commit`;
`BridgeUploads` принимает root store, registry, лимиты; все методы требуют keyId и ct.
Манифест: destination, files[{id,relativePath,size,sha256}], directories[], replace[];
ответ: upload_id, conflicts, confirmed_offsets, expires_at, статусы отдельных файлов.

- [ ] UploadCases: оборвать после первого блока, открыть session новым клиентом,
  продолжить; неверный offset не меняет файл, неверный SHA не публикуется.
  Fixture методы `UploadManifestAsync(relativePath,bytes)`, `PutBlockAsync(id,offset,bytes)`,
  `CommitUploadAsync(id)`, `ReadUploadedAsync(relativePath)` направляют запросы реальному HTTPS-хосту.
```csharp
byte[] data = [1, 2, 3, 4];
var id = await f.UploadManifestAsync("nested/файл.bin", data);
await f.PutBlockAsync(id, 0, data[..2]);
await f.PutBlockAsync(id, 2, data[2..]);
await f.CommitUploadAsync(id);
Check((await f.ReadUploadedAsync("nested/файл.bin")).SequenceEqual(data), "upload corrupted");
```
- [ ] Runner `--case uploads` → FAIL; реализовать блоки 4 MiB, 2/connection и 4/server,
  общий rate limiter 8 MiB/s, отдельные от JSON/API-control slots; потоковые fixed buffers.
  Сессии и offsets писать durable, временные файлы на томе назначения;
  default limits: 10000 файлов, 8 MiB manifest, 20 GiB/file, staging quota
  40 GiB на сервер с проверкой свободного места. Показывать эти настройки в /info и Config.
  Очистка через 24 часа и при cancel/revoke; после рестарта сверять размер stage с offset.
- [ ] Путь нормализовать и проверять сегменты, case collision, ADS, absolute/..,
  symlink/junction. На Windows открыть destination directory handles без обхода
  reparse points и сверить финальные пути/идентичность перед publication; если ОС
  не позволяет надёжно исключить подмену, отклонить назначение, а не продолжать запись.
  Не создавать ZIP extraction surface. commit атомарен на файл; повтор того же commit
  возвращает сохранённые результаты. Замена требует подтверждённого fingerprint старого файла.
- [ ] PASS: занятый файл/нет места/отзыв/подмена junction в момент commit,
  неизменность старых байт при неуспехе, пустые папки, другой ключ не видит session.
  На Linux повторить безопасные path cases; непроверенный таргет указать в отчёте.
- [ ] Коммит `feat: upload remote project files with resumable verified transfers`.

## Задача 10. Загрузка файлов/папок из интерфейса

**Files:** создать `wwwroot/js/tasker-upload.js`, `tests/tasker.upload.test.js`;
изменить `wwwroot/tasker.html`, `wwwroot/css/tasker.css`, `tasker-files.js`,
`Handlers/BridgeLocalHandler.cs`, `Controllers/Bridge/BridgeClient.cs`.
**Interfaces:** `createUploadController({send,readBlock,hashFile})` с
`start({connectionId,destination,files,directories})`, `resume(uploadId)`, `cancel(uploadId)`;
node ID/connection ID неизменны для всей сессии, даже после переключения Tasker.

- [ ] Node tests: второй блок отправляется с offset первого; retry сверяет server
  offsets; node switch не меняет destination connection; кнопка overwrite ждёт выбора.
```javascript
const requests = [];
const upload = createUploadController(fakeUploadDeps(requests));
await upload.start({ connectionId: 'A', destination: 'D:\\Scripts', files: twoBlockFiles(), directories: [] });
assert.deepEqual(requests.filter(x => x.kind === 'block').map(x => x.offset), [0, 4194304]);
```
  Определить fakeUploadDeps и twoBlockFiles в тесте: первый даёт реальные session/offset
  ответы, второй — File из 4194305 байт; блоки обрабатываются через Blob.slice.
- [ ] Запустить `rtk proxy node --test tests/tasker.upload.test.js` → FAIL.
- [ ] Реализовать выбор файлов и дерева папки через доступные WebView/browser API;
  пустые папки извлекать directory entries API, при отсутствии такого API использовать
  локальный системный picker через backend и получить манифест с directory entries.
  Backend выдаёт upload-scoped opaque handle вместо приёма произвольного локального пути
  из браузерного запроса; SHA-256 считать потоком на backend для native selection,
  для File источника — последовательно по блокам, без whole-file ArrayBuffer.
  Показывать skipped links, destination node, конфликты, размер, прогресс, cancel/resume.
  Большие файлы браузера передаются блоками через backend; plaintext key UI не получает.
- [ ] Выбор «новый каталог» по умолчанию для обновления проекта работающей задачи;
  успешная загрузка предлагает явно выбрать script_path и сохранить задачу.
  Не менять задачу/перезапускать автоматически. 20 GiB файл не должен занимать 20 GiB RAM.
- [ ] PASS Node; в браузере загрузить дерево с Unicode/пустой папкой, оборвать сеть,
  восстановить, проверить SHA-256 через серверный file fixture и выбранный путь задачи.
- [ ] Коммит `feat: transfer local script folders from the Tasker interface`.

## Задача 11. Ограниченные логи и наблюдение

**Files:** создать `Controllers/Bridge/BridgeOutput.cs`, `Handlers/BridgeOutputApi.cs`,
`wwwroot/js/tasker-remote-output.js`, `tests/tasker.remote-output.test.js`,
`tests/Bridge.Integration/OutputCases.cs`; изменить `Controllers/SchedulerService.cs`,
`Controllers/SseHub.cs`, `wwwroot/js/tasker.js`, `BridgeLocalHandler.cs`.
**Interfaces:** `BridgeOutput.Read(taskId,runId,cursor,limit)` → JSON page с gap/next_cursor;
`BridgeOutput.Subscribe(keyId,taskId,runId,ct)` → поток событий через authenticated fetch proxy.
Reader использует ограниченные снимки существующего output, не копирует всю историю.

- [ ] OutputCases: медленный subscriber, миллион байт без newline, разрезанный UTF-8,
  истёкший cursor и revoke. `BridgeOutput` конструктор принимает maxBytes/maxLines,
  `Append(taskId,runId,ReadOnlyMemory<byte>)` принимает данные; тест использует малые лимиты.
```csharp
var output = new BridgeOutput(maxBytes: 262144, maxLines: 2000);
output.Append("task", "run", new byte[1000000]);
var page = output.Read("task", "run", null, 200);
Check(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(page).Length <= 262144, "unbounded page");
```
- [ ] Runner `--case output` → FAIL; от источника выдавать данные неблокирующе,
  decoder сохраняет неполный UTF-8 до следующего блока; длинную строку делить на
  byte-bounded фрагменты без повреждения символов. Общий payload ответа ≤256 KiB,
  лимит считать включая JSON encoding/metadata, а не только исходные строки.
  Привязать к output pipeline scheduler, не менять семантику локальных существующих логов.
- [ ] Initial/page максимум 200 строк/256 KiB; 1 stream/client, 64 KiB/s;
  очередь 256 KiB, drop-oldest+gap marker; TTL 15 минут без активности и общий memory
  cap 64 MiB для bridge output, при вытеснении сообщать gap. Клиент ring 2000 строк.
  В /info объявить лимиты; queue пер-key revoke, ct и disconnect освобождает подписку.
  Контроль и file transfer используют отдельные limiter pools, без общего backlog.
- [ ] JS тестирует off по умолчанию, explicit start/stop, DOM≤2000, old generation,
  visible gap, отсутствие traffic polling и выход из вкладки/выбранной задачи.
  `rtk proxy node --test tests/tasker.remote-output.test.js` → PASS;
  C# output PASS; local output/filter regression PASS.
- [ ] Коммит `feat: bound remote task output and opt-in live observation`.

## Задача 12. Сквозной прогон, нагрузка, публикация и документация

**Files:** создать `tests/Bridge.Integration/EndToEndCases.cs`, `LoadCases.cs`,
`tests/bridge.browser-smoke.js`, `docs-vault/Navigation/Pages/Remote Tasker.md`,
`docs-vault-en/Navigation/Pages/Remote Tasker.md`; изменить страницы Tasker/Config
в обеих docs-vault и `Cronopus.csproj` только для исключения bridge secrets/staging из publish.
**Interfaces:** runner `--case e2e`, `--case load --minutes 15`;
Browser test принимает `BRIDGE_LOCAL_URL`, `BRIDGE_SERVER_URL` из fixture и не использует default ports.

- [ ] Сценарий: два независимых процесса, отдельные store/SQLite БД, соединение,
  folder upload → серверный picker → task/settings/payload/schedule → run/pause/stop;
  рестарт сервера, повторное чтение, revoked key и непроходимые чужие маршруты.
  Тестовые процессы запускает runner с app копиями в temp; скрытые окна Windows,
  сохраняет собственные PID и завершает только их. Тестовые скрипты безопасны и короткие.
```csharp
await f.RunTwoProcessScenarioAsync();
Check(await f.ServerTaskExistsAsync() && !await f.LocalTaskExistsAsync(), "wrong node mutation");
```
- [ ] Запустить e2e до финальной сборки; каждый discovered regression закрепить тестом.
  Runner методы RunTwoProcessScenarioAsync/ServerTaskExistsAsync/LocalTaskExistsAsync
  реализовать в BridgeFixture с запросами к двум backend и проверкой их отдельных БД.
- [ ] LoadCases: 1000 синтетических running instances через тестовый источник
  состояния, активные output producers, параллельный upload и команды. Продуктовый
  API не получает маршрут генерации synthetic state. Писать JSON-отчёт: параметры
  стенда, p95, CPU/RAM, bytes/sec, queue sizes, gaps и error counts за 15 минут.
  15s timeout клиентских запросов, медленный consumer и перезапуск входят в прогон.
  Цель p95<1s при RTT≤100ms; сообщать отдельно, тестировалась ли реальная WAN.
  Synthetic проверка не доказывает запуск тысячи внешних процессов.
- [ ] Запустить существующие JS tests и новые bridge tests, C# suite и builds:
```powershell
rtk proxy node --test tests/*.test.js
rtk dotnet run --project tests/Bridge.Integration/Bridge.Integration.csproj -- --case all
rtk dotnet build Cronopus.csproj -c Debug -f net10.0 --output C:/Users/l3gi0n/AppData/Local/Temp/cronopus-bridge-net10
rtk dotnet build Cronopus.csproj -c Debug -f net10.0-windows --output C:/Users/l3gi0n/AppData/Local/Temp/cronopus-bridge-windows
rtk git diff --check
```
  Поддержать glob в JS runner через shell/runtime по установленной версии Node;
  если glob не разворачивается, передать список файлов отдельными аргументами через PowerShell.
- [ ] Publish self-contained win-x64 в отдельный temp output без остановки текущего
  приложения; проверить FrameworkReference runtime assets и отсутствие store, private
  keys, operation journal и staging в publish. Не выпускать релиз и не менять версию.
  Запустить опубликованные bytes на двух тестовых узлах, повторить browser smoke.
- [ ] Документировать IP setup/RDP bootstrap, port/firewall, pin renewal, key revoke,
  offline/unknown outcomes, server timezone, uploads/conflicts, log limits и troubleshooting.
  С другой машины выполнить HTTPS connection + harmless task CRUD. Если доступа к
  реальному серверу нет, сохранить проверенный local результат и назвать WAN непроверенным;
  не утверждать доступность публичного порта по localhost smoke.
- [ ] Финально сверить spec §9, сохранить фактические результаты в
  `docs/superpowers/verification/2026-10-15-tasker-remote-bridge.md` без секретов.
  Коммит `test: verify remote Tasker bridge workflows and delivery artifacts`.

## Исполнение и критерий завершения

Зависимости: 1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9 → 10 → 11 → 12.
Рекомендуемый способ — выполнение в этой сессии: соседние этапы используют общие
контракты, а смена исполнителя на каждом этапе потребует передачи большого контекста.
При выборе пользователем делегирования исполнение и ревью выполнять по выбранному skill.
До реализации пользователь проверяет этот письменный план и выбирает способ исполнения.
Промежуточные PASS отражают только законченные этапы. Готовность всей фичи требует
полного сквозного прогона, сохранённых результатов и явного указания непроверенных сред.
