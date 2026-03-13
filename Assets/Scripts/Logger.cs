using System;
using System.IO;
using UnityEngine;

namespace Logger
{

public enum LogLevel
{
    Trace,
    Debug,
    Info,
    Warning,
    Error,
    Critical
}

public static class Logger
{
    private static LogLevel currentLogLevel = LogLevel.Info;
    private static string logFilePath = "./Assets/Scripts/Logs/logs.txt";

    // Initiale Konfiguration des statischen Loggers.
    public static void Configure(LogLevel logLevel = LogLevel.Info, string filePath = "./Assets/Scripts/Logs/logs.txt")
    {
        currentLogLevel = logLevel;
        logFilePath = filePath;
    }

    // Methode, um Log-Nachrichten anzuzeigen und in eine Datei zu schreiben
    private static void Log(LogLevel level, string message)
    {
        if (level < currentLogLevel)
            return; // Wenn der Log-Level kleiner als der aktuelle Log-Level ist, nichts tun

        string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";

        // Log-Nachricht in die Konsole (Unity-spezifisch)
        switch (level)
        {
            case LogLevel.Trace:
                Debug.Log(logMessage);
                break;
            case LogLevel.Debug:
                Debug.Log(logMessage);
                break;
            case LogLevel.Info:
                Debug.Log(logMessage);
                break;
            case LogLevel.Warning:
                Debug.LogWarning(logMessage);
                break;
            case LogLevel.Error:
                Debug.LogError(logMessage);
                break;
            case LogLevel.Critical:
                Debug.LogError($"[CRITICAL] {logMessage}");
                break;
        }

        // Log-Nachricht in eine Datei speichern (wenn in der Produktion gewünscht)
        //WriteLogToFile(logMessage);
    }

    // Methode zum Schreiben des Logs in eine Datei
    private static void WriteLogToFile(string message)
    {
        try
        {
            File.AppendAllText(logFilePath, message + Environment.NewLine);
        }
        catch (Exception ex)
        {
            // Falls das Schreiben in die Datei fehlschlägt, fügen wir die Fehlermeldung zu den Logs hinzu
            Debug.LogError($"Fehler beim Schreiben in Log-Datei: {ex.Message}");
        }
    }

    // Öffentlich zugängliche Methoden für die verschiedenen Log-Level
    public static void DebugLog(string message)
    {
        Log(LogLevel.Debug, message);
    }

    public static void InfoLog(string message)
    {
        Log(LogLevel.Info, message);
    }

    public static void WarningLog(string message)
    {
        Log(LogLevel.Warning, message);
    }

    public static void ErrorLog(string message)
    {
        Log(LogLevel.Error, message);
    }

    public static void CriticalLog(string message)
    {
        Log(LogLevel.Critical, message);
    }
    
    // Dynamische Anpassung des Log-Levels zur Laufzeit
    public static void SetLogLevel(LogLevel newLogLevel)
    {
        currentLogLevel = newLogLevel;
    }
}
}