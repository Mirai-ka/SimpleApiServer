using System.Net;
using System.Text;
using System.Text.Json;
using System.Linq;

// ========== МОДЕЛИ ==========

public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsCompleted { get; set; }
    public int Priority { get; set; } = 3;        // Новое поле: 1-5
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // Новое поле
}

// DTO для валидации (отдельно от модели)
public class CreateTaskRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public bool? IsCompleted { get; set; }
    public int? Priority { get; set; }
}

// ========== ОСНОВНОЙ КЛАСС ==========

class Program
{
    private static readonly List<TaskItem> tasks = new()
    {
        new() { Id = 1, Title = "Задача 1", Description = "Описание задачи 1", IsCompleted = false, Priority = 2, CreatedAt = DateTime.UtcNow.AddDays(-5) },
        new() { Id = 2, Title = "Задача 2", Description = "Описание задачи 2", IsCompleted = true, Priority = 4, CreatedAt = DateTime.UtcNow.AddDays(-2) }
    };

    static void Main()
    {
        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:5000/");
        listener.Start();
        Console.WriteLine("=== SimpleApiServer с валидацией ===");
        Console.WriteLine("API сервер запущен: http://localhost:5000/api/tasks");
        Console.WriteLine("Поддерживаемые операции:");
        Console.WriteLine("  GET    /api/tasks           - получить все задачи");
        Console.WriteLine("  GET    /api/tasks/{id}      - получить задачу по ID");
        Console.WriteLine("  POST   /api/tasks           - создать задачу (с валидацией)");
        Console.WriteLine("  PUT    /api/tasks/{id}      - обновить задачу (с валидацией)");
        Console.WriteLine("  DELETE /api/tasks/{id}      - удалить задачу");
        Console.WriteLine("\nНажмите Ctrl+C для остановки\n");

        try
        {
            while (true)
            {
                var context = listener.GetContext();
                var request = context.Request;
                var response = context.Response;

                var path = request.Url.AbsolutePath;
                var parts = path.Trim('/').Split('/').Where(p => !string.IsNullOrEmpty(p)).ToArray();

                if (parts.Length == 2 && parts[0] == "api" && parts[1] == "tasks")
                {
                    if (request.HttpMethod == "GET")
                        HandleGetTasks(response);
                    else if (request.HttpMethod == "POST")
                        HandleCreateTask(request, response);
                    else
                        SendError(response, 405, "Method Not Allowed");
                }
                else if (parts.Length == 3 && parts[0] == "api" && parts[1] == "tasks" && int.TryParse(parts[2], out int id))
                {
                    switch (request.HttpMethod)
                    {
                        case "GET": HandleGetTaskById(response, id); break;
                        case "PUT": HandleUpdateTask(request, response, id); break;
                        case "DELETE": HandleDeleteTask(response, id); break;
                        default: SendError(response, 405, "Method Not Allowed"); break;
                    }
                }
                else
                {
                    SendError(response, 404, "Not Found");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка: {ex.Message}");
        }
        finally
        {
            listener.Stop();
        }
    }

    // ========== GET ОБРАБОТЧИКИ ==========

    static void HandleGetTasks(HttpListenerResponse res)
    {
        var tasksList = tasks.Select(t => new
        {
            t.Id,
            t.Title,
            t.Description,
            t.IsCompleted,
            t.Priority,
            t.CreatedAt
        });
        SendJson(res, JsonSerializer.Serialize(tasksList), 200);
    }

    static void HandleGetTaskById(HttpListenerResponse res, int id)
    {
        var task = tasks.FirstOrDefault(t => t.Id == id);
        if (task == null)
        {
            SendError(res, 404, "Task not found");
        }
        else
        {
            SendJson(res, JsonSerializer.Serialize(task), 200);
        }
    }

    // ========== POST С ВАЛИДАЦИЕЙ ==========

    static void HandleCreateTask(HttpListenerRequest req, HttpListenerResponse res)
    {
        // 1. Чтение тела запроса
        string body;
        using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
            body = reader.ReadToEnd();

        // 2. Парсинг JSON
        CreateTaskRequest? data;
        try
        {
            data = JsonSerializer.Deserialize<CreateTaskRequest>(body);
        }
        catch (JsonException)
        {
            SendValidationError(res, new List<object> { new { field = "JSON", message = "Некорректный JSON формат" } });
            return;
        }

        if (data == null)
        {
            SendValidationError(res, new List<object> { new { field = "JSON", message = "Тело запроса не может быть пустым" } });
            return;
        }

        // 3. Сбор ошибок валидации
        var errors = new List<object>();

        // Валидация Title (обязательное поле, 1-200 символов)
        if (string.IsNullOrWhiteSpace(data.Title))
            errors.Add(new { field = "Title", message = "Название обязательно" });
        else if (data.Title.Length > 200)
            errors.Add(new { field = "Title", message = "Название не может превышать 200 символов" });

        // Валидация Description (необязательное, но если есть - не более 1000 символов)
        if (!string.IsNullOrEmpty(data.Description) && data.Description.Length > 1000)
            errors.Add(new { field = "Description", message = "Описание не может превышать 1000 символов" });

        // Валидация Priority (если указано - от 1 до 5)
        if (data.Priority.HasValue && (data.Priority.Value < 1 || data.Priority.Value > 5))
            errors.Add(new { field = "Priority", message = "Приоритет должен быть от 1 до 5" });

        // 4. Если есть ошибки - возвращаем 400
        if (errors.Count > 0)
        {
            SendValidationError(res, errors);
            return;
        }

        // 5. Создание задачи (все поля валидны)
        var newTask = new TaskItem
        {
            Id = tasks.Any() ? tasks.Max(t => t.Id) + 1 : 1,
            Title = data.Title!,
            Description = data.Description,
            IsCompleted = data.IsCompleted ?? false,
            Priority = data.Priority ?? 3,
            CreatedAt = DateTime.UtcNow
        };

        tasks.Add(newTask);
        res.Headers.Add("Location", $"http://localhost:5000/api/tasks/{newTask.Id}");
        SendJson(res, JsonSerializer.Serialize(newTask), 201);
        
        Console.WriteLine($"[POST] Создана задача #{newTask.Id}: {newTask.Title}");
    }

    // ========== PUT С ВАЛИДАЦИЕЙ ==========

    static void HandleUpdateTask(HttpListenerRequest req, HttpListenerResponse res, int id)
    {
        // 1. Поиск задачи
        var task = tasks.FirstOrDefault(t => t.Id == id);
        if (task == null)
        {
            SendError(res, 404, "Task not found");
            return;
        }

        // 2. Чтение тела запроса
        string body;
        using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
            body = reader.ReadToEnd();

        // 3. Парсинг JSON
        CreateTaskRequest? data;
        try
        {
            data = JsonSerializer.Deserialize<CreateTaskRequest>(body);
        }
        catch (JsonException)
        {
            SendValidationError(res, new List<object> { new { field = "JSON", message = "Некорректный JSON формат" } });
            return;
        }

        if (data == null)
        {
            SendValidationError(res, new List<object> { new { field = "JSON", message = "Тело запроса не может быть пустым" } });
            return;
        }

        // 4. Сбор ошибок валидации (только для переданных полей)
        var errors = new List<object>();

        // Валидация Title (если передан)
        if (data.Title != null)
        {
            if (string.IsNullOrWhiteSpace(data.Title))
                errors.Add(new { field = "Title", message = "Название не может быть пустым" });
            else if (data.Title.Length > 200)
                errors.Add(new { field = "Title", message = "Название не может превышать 200 символов" });
        }

        // Валидация Description (если передан)
        if (data.Description != null && data.Description.Length > 1000)
            errors.Add(new { field = "Description", message = "Описание не может превышать 1000 символов" });

        // Валидация Priority (если передан)
        if (data.Priority.HasValue && (data.Priority.Value < 1 || data.Priority.Value > 5))
            errors.Add(new { field = "Priority", message = "Приоритет должен быть от 1 до 5" });

        // 5. Если есть ошибки - возвращаем 400
        if (errors.Count > 0)
        {
            SendValidationError(res, errors);
            return;
        }

        // 6. Обновление задачи (только переданных полей)
        if (data.Title != null) task.Title = data.Title;
        if (data.Description != null) task.Description = data.Description;
        if (data.IsCompleted.HasValue) task.IsCompleted = data.IsCompleted.Value;
        if (data.Priority.HasValue) task.Priority = data.Priority.Value;

        SendJson(res, JsonSerializer.Serialize(task), 200);
        
        Console.WriteLine($"[PUT] Обновлена задача #{id}: {task.Title}");
    }

    // ========== DELETE ОБРАБОТЧИК ==========

    static void HandleDeleteTask(HttpListenerResponse res, int id)
    {
        var task = tasks.FirstOrDefault(t => t.Id == id);
        if (task == null)
        {
            SendError(res, 404, "Task not found");
            return;
        }

        tasks.Remove(task);
        SendJson(res, "{}", 200);
        
        Console.WriteLine($"[DELETE] Удалена задача #{id}: {task.Title}");
    }

    // ========== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ==========

    static void SendJson(HttpListenerResponse res, string json, int statusCode)
    {
        res.StatusCode = statusCode;
        res.ContentType = "application/json; charset=utf-8";
        var buffer = Encoding.UTF8.GetBytes(json);
        res.ContentLength64 = buffer.Length;
        res.OutputStream.Write(buffer, 0, buffer.Length);
        res.OutputStream.Close();
    }

    static void SendError(HttpListenerResponse res, int code, string message)
    {
        var json = JsonSerializer.Serialize(new { error = message });
        SendJson(res, json, code);
    }

    // Единый формат ошибки валидации (как требуется в ЛР 12.2)
    static void SendValidationError(HttpListenerResponse res, List<object> errors)
    {
        var errorResponse = new
        {
            error = "Ошибка валидации",
            errors = errors
        };
        SendJson(res, JsonSerializer.Serialize(errorResponse), 400);
    }
}