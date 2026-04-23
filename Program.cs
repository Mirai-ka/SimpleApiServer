using System.Net;
using System.Text;
using System.Text.Json;
using System.Linq;

public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsCompleted { get; set; }
}

class Program
{
    private static readonly List<TaskItem> tasks = new()
    {
        new() { Id = 1, Title = "Задача 1", IsCompleted = false },
        new() { Id = 2, Title = "Задача 2", IsCompleted = true }
    };

    static void Main()
    {
        var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:5000/");
        listener.Start();
        Console.WriteLine("API сервер запущен: http://localhost:5000/api/tasks");
        Console.WriteLine("Нажмите Ctrl+C для остановки");

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
                        HandleGetTasks(request, response);
                    else if (request.HttpMethod == "POST")
                        HandleCreateTask(request, response);
                    else
                        SendError(response, 405, "Method Not Allowed");
                }
                else if (parts.Length == 3 && parts[0] == "api" && parts[1] == "tasks" && int.TryParse(parts[2], out int id))
                {
                    switch (request.HttpMethod)
                    {
                        case "GET": HandleGetTaskById(request, response, id); break;
                        case "PUT": HandleUpdateTask(request, response, id); break;
                        case "DELETE": HandleDeleteTask(request, response, id); break;
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

    static void HandleGetTasks(HttpListenerRequest req, HttpListenerResponse res) => SendJson(res, JsonSerializer.Serialize(tasks.ToList()), 200);
    static void HandleGetTaskById(HttpListenerRequest req, HttpListenerResponse res, int id)
    {
        var task = tasks.FirstOrDefault(t => t.Id == id);
        if (task == null) SendJson(res, JsonSerializer.Serialize(new { error = "Task not found", id }), 404);
        else SendJson(res, JsonSerializer.Serialize(task), 200);
    }
    static void HandleCreateTask(HttpListenerRequest req, HttpListenerResponse res)
    {
        try
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            var body = reader.ReadToEnd();
            var newTask = JsonSerializer.Deserialize<TaskItem>(body) ?? throw new Exception("Invalid JSON");
            if (string.IsNullOrWhiteSpace(newTask.Title)) throw new Exception("Title required");

            newTask.Id = tasks.Any() ? tasks.Max(t => t.Id) + 1 : 1;
            tasks.Add(newTask);
            res.Headers.Add("Location", $"http://localhost:5000/api/tasks/{newTask.Id}");
            SendJson(res, JsonSerializer.Serialize(newTask), 201);
        }
        catch (Exception ex)
        {
            SendJson(res, JsonSerializer.Serialize(new { error = ex.Message }), 400);
        }
    }
    static void HandleUpdateTask(HttpListenerRequest req, HttpListenerResponse res, int id)
    {
        var task = tasks.FirstOrDefault(t => t.Id == id);
        if (task == null) { SendJson(res, JsonSerializer.Serialize(new { error = "Task not found" }), 404); return; }
        try
        {
            using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
            var body = reader.ReadToEnd();
            var update = JsonSerializer.Deserialize<TaskItem>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            task.Title = update?.Title ?? task.Title;
            task.Description = update?.Description;
            task.IsCompleted = update?.IsCompleted ?? task.IsCompleted;
            SendJson(res, JsonSerializer.Serialize(task), 200);
        }
        catch (Exception ex)
        {
            SendJson(res, JsonSerializer.Serialize(new { error = ex.Message }), 400);
        }
    }
    static void HandleDeleteTask(HttpListenerRequest req, HttpListenerResponse res, int id)
    {
        var task = tasks.FirstOrDefault(t => t.Id == id);
        if (task == null) { SendJson(res, JsonSerializer.Serialize(new { error = "Task not found" }), 404); return; }
        tasks.Remove(task);
        SendJson(res, "{}", 200);
    }

    static void SendJson(HttpListenerResponse res, string json, int statusCode)
    {
        res.StatusCode = statusCode;
        res.ContentType = "application/json; charset=utf-8";
        var buffer = Encoding.UTF8.GetBytes(json);
        res.OutputStream.Write(buffer, 0, buffer.Length);
        res.OutputStream.Close();
    }

    static void SendError(HttpListenerResponse res, int code, string message)
    {
        res.StatusCode = code;
        var json = JsonSerializer.Serialize(new { error = message });
        var buffer = Encoding.UTF8.GetBytes(json);
        res.ContentType = "application/json; charset=utf-8";
        res.OutputStream.Write(buffer, 0, buffer.Length);
        res.OutputStream.Close();
    }
}
