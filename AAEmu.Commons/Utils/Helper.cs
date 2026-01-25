using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace AAEmu.Commons.Utils;

public static class Helper
{
    // Було: private static Dictionary<Tuple<object, int>, DateTime> intervals = new();
    private static readonly ConcurrentDictionary<Tuple<object, int>, DateTime> intervals = new();

    /// <summary>
    /// Возвращает true если прошло не менее указанного интервала времени (после предыдущего срабатывания)
    /// </summary>
    public static bool CheckInterval(
        this object caller,
        int interval = 1000,
        [CallerLineNumber] int lineNumber = 0)
    {
        if (caller == null) return true; // на всякий случай

        var now = DateTime.UtcNow;
        var key = new Tuple<object, int>(caller, lineNumber);

        // Спроба отримати значення без блокування
        if (intervals.TryGetValue(key, out var next) && next > now)
        {
            return false;
        }

        // Обчислюємо нове значення наступного часу
        var newNext = now.AddMilliseconds(interval);

        // Додаємо або оновлюємо значення (ConcurrentDictionary це робить атомарно)
        intervals[key] = newNext;

        return true;
    }

    // Було: private static ConcurrentDictionary<Tuple<object, int>, bool> conditions = new();
    // Цей вже був Concurrent — залишаємо як є, або міняємо на readonly для стилю
    private static readonly ConcurrentDictionary<Tuple<object, int>, bool> conditions = new();

    /// <summary>
    /// Возвращает true если условие изменилось с false на true
    /// </summary>
    public static bool Triggered(
        this object sender,
        bool condition,
        [CallerLineNumber] int lineNumber = 0)
    {
        var key = new Tuple<object, int>(sender, lineNumber);

        // TryGetValue є безпечним
        conditions.TryGetValue(key, out var old); // якщо не знайдено → old = false (default(bool))

        // Оновлюємо значення (атомарно)
        conditions[key] = condition;

        return condition && !old;
    }
}
