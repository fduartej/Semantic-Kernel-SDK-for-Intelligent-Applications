using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using SKWebChatBot.Data;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

#pragma warning disable SKEXP0010 // Suprimir advertencia sobre método experimental

namespace SKWebChatBot.Services;

public class SemanticKernelService
{
    private readonly ILogger<SemanticKernelService> _logger;
    private readonly string _endpoint;
    private readonly string _modelName;
    private readonly HttpClient _httpClient;

    public SemanticKernelService(IConfiguration configuration, ILogger<SemanticKernelService> logger)
    {
        _logger = logger;
        _endpoint = configuration["SemanticKernel:Endpoint"] ?? "http://localhost:11434";
        _modelName = configuration["SemanticKernel:ModelName"] ?? "llama2:latest";

        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(60); // Aumentado a 60s para acomodar los ~22s de respuesta
        _httpClient.BaseAddress = new Uri(_endpoint);

        _logger.LogInformation("Configurando Ollama - Endpoint: {Endpoint}, Modelo: {ModelName}, Timeout: 60s", _endpoint, _modelName);
    }

    public async Task<string> GetChatResponseAsync(string userMessage)
    {
        _logger.LogInformation("Enviando mensaje básico a Ollama: {Message}", userMessage);
        try
        {
            var requestBody = new
            {
                model = _modelName,
                stream = false,
                messages = new[]
                {
                    new { role = "user", content = userMessage }
                }
            };
            
            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            _logger.LogInformation("Enviando petición a Ollama API: {Endpoint}/api/chat", _endpoint);
            
            var response = await _httpClient.PostAsync("/api/chat", content);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Respuesta recibida de Ollama exitosamente");
                
                // Parse de la respuesta de Ollama
                using var doc = JsonDocument.Parse(responseContent);
                if (doc.RootElement.TryGetProperty("message", out var messageElement))
                {
                    if (messageElement.TryGetProperty("content", out var contentElement))
                    {
                        return contentElement.GetString() ?? "Sin respuesta";
                    }
                }
                return "Sin respuesta válida";
            }
            else
            {
                _logger.LogError("Error en respuesta de Ollama: {StatusCode}", response.StatusCode);
                return "Error al comunicarse con Ollama";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener respuesta básica de Ollama");
            return "Error: " + ex.Message;
        }
    }

    public async Task<string> GetChatResponseWithHistoryAsync(string userMessage, IEnumerable<ChatMessage> messages)
    {
        _logger.LogInformation("Enviando mensaje con historial a Ollama: {Message}", userMessage);
        try
        {
            var messagesList = new List<object>();
            
            // Agregar mensajes del historial
            foreach (var message in messages)
            {
                var role = message.Sender == "User" ? "user" : "assistant";
                messagesList.Add(new { role = role, content = message.Message });
            }
            
            // Agregar el mensaje actual del usuario
            messagesList.Add(new { role = "user", content = userMessage });
            
            var requestBody = new
            {
                model = _modelName,
                stream = false,
                messages = messagesList.ToArray()
            };
            
            var json = JsonSerializer.Serialize(requestBody);
            var requestContent = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("/api/chat", requestContent);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Respuesta con historial recibida de Ollama exitosamente");
                
                // Parse de la respuesta de Ollama
                using var doc = JsonDocument.Parse(responseContent);
                if (doc.RootElement.TryGetProperty("message", out var messageElement))
                {
                    if (messageElement.TryGetProperty("content", out var contentElement))
                    {
                        return contentElement.GetString() ?? "Sin respuesta";
                    }
                }
                return "Sin respuesta válida";
            }
            else
            {
                _logger.LogError("Error en respuesta con historial de Ollama: {StatusCode}", response.StatusCode);
                return "Error al comunicarse con Ollama";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener respuesta con historial de Ollama");
            return "Error: " + ex.Message;
        }
    }
    
    public async Task<string> GetChatResponseWithRagAsync(string userMessage)
    {
        _logger.LogInformation("Enviando mensaje RAG a Ollama: {Message}", userMessage);
        try
        {
            string pdfPath = Path.Combine(Directory.GetCurrentDirectory(), "Documents", "career-profiles.pdf");
            string content = ExtractTextFromPdf(pdfPath);

            var prompt = $"""
                    You are a career guidance assistant. You will be provided with a list of career profiles, each containing a title and a description.

                    Your task is to help users find suitable career options based on their interests and skills. When a user provides their interests or skills, identify and list the careers that align with the provided information. If no matching careers are found, respond with, "I couldn't find any careers matching your interests and skills."

                    Here is the list of career profiles:

                    {content}

                    User's interests and skills: {userMessage}
                    """;
            
            _logger.LogInformation("Enviando prompt RAG con {Length} caracteres", prompt.Length);
            
            var requestBody = new
            {
                model = _modelName,
                stream = false,
                messages = new[]
                {
                    new { role = "user", content = prompt }
                }
            };
            
            var json = JsonSerializer.Serialize(requestBody);
            var requestContent = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync("/api/chat", requestContent);
            
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Respuesta RAG recibida de Ollama exitosamente");
                
                // Parse de la respuesta de Ollama
                using var doc = JsonDocument.Parse(responseContent);
                if (doc.RootElement.TryGetProperty("message", out var messageElement))
                {
                    if (messageElement.TryGetProperty("content", out var contentElement))
                    {
                        return contentElement.GetString() ?? "Sin respuesta";
                    }
                }
                return "Sin respuesta válida";
            }
            else
            {
                _logger.LogError("Error en respuesta RAG de Ollama: {StatusCode}", response.StatusCode);
                return "Error al comunicarse con Ollama";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener respuesta RAG de Ollama");
            return "Error: " + ex.Message;
        }
    }

    private static string ExtractTextFromPdf(string pdfPath)
    {
        var text = new StringBuilder();

        using (var document = PdfDocument.Open(pdfPath))
        {
            foreach (var page in document.GetPages())
            {
                var pageText = ContentOrderTextExtractor.GetText(page);
                text.AppendLine(pageText);
            }
        }

        return text.ToString();
    }
}
