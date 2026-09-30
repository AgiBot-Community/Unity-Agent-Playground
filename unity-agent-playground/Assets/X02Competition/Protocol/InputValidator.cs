using System;
using System.Collections.Generic;
using System.Globalization;

namespace X02Competition.Protocol
{
    /// <summary>
    /// 输入验证工具类，用于参数范围检查和格式验证。
    /// </summary>
    public static class InputValidator
    {
        /// <summary>
        /// 验证浮点参数是否在指定范围内。
        /// </summary>
        /// <param name="value">待验证的值</param>
        /// <param name="min">最小值</param>
        /// <param name="max">最大值</param>
        /// <param name="paramName">参数名称（用于错误消息）</param>
        /// <returns>限制在范围内的值</returns>
        public static float ClampFloat(float value, float min, float max, string paramName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentException($"Invalid {paramName}: must be a valid number");
            }
            // 手动实现 Clamp，避免依赖 UnityEngine
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// 验证整数参数是否在指定范围内。
        /// </summary>
        public static int ClampInt(int value, int min, int max, string paramName)
        {
            if (value < min || value > max)
            {
                throw new ArgumentOutOfRangeException(paramName,
                    $"{paramName} must be between {min} and {max}, got {value}");
            }
            return value;
        }

        /// <summary>
        /// 验证字符串不为空或 null。
        /// </summary>
        public static string RequireNonEmpty(string value, string paramName)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException($"{paramName} cannot be null or empty");
            }
            return value;
        }

        /// <summary>
        /// 验证字符串长度在指定范围内。
        /// </summary>
        public static string ValidateStringLength(string value, int minLength, int maxLength, string paramName)
        {
            if (value == null)
            {
                throw new ArgumentNullException(paramName);
            }
            if (value.Length < minLength || value.Length > maxLength)
            {
                throw new ArgumentException(
                    $"{paramName} length must be between {minLength} and {maxLength}, got {value.Length}");
            }
            return value;
        }

        /// <summary>
        /// 从字典中安全提取浮点参数，带默认值和范围验证。
        /// </summary>
        public static float GetFloatParam(Dictionary<string, object> parameters, string key,
            float defaultValue, float min, float max)
        {
            if (parameters == null || !parameters.TryGetValue(key, out var value))
            {
                return defaultValue;
            }

            if (value == null || value is bool || !(value is IConvertible))
                throw new ArgumentException($"Invalid {key}: must be a number", key);
            try
            {
                float floatValue = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                return ClampFloat(floatValue, min, max, key);
            }
            catch (Exception error) when (error is FormatException ||
                error is InvalidCastException || error is OverflowException)
            {
                throw new ArgumentException($"Invalid {key}: must be a finite number", key, error);
            }
        }

        /// <summary>
        /// 从字典中安全提取整数参数，带默认值和范围验证。
        /// </summary>
        public static int GetIntParam(Dictionary<string, object> parameters, string key,
            int defaultValue, int min, int max)
        {
            if (parameters == null || !parameters.TryGetValue(key, out var value))
            {
                return defaultValue;
            }

            if (value == null || value is bool || !(value is IConvertible))
                throw new ArgumentException($"Invalid {key}: must be a number", key);
            try
            {
                int intValue = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return Math.Max(min, Math.Min(max, intValue));
            }
            catch (Exception error) when (error is FormatException ||
                error is InvalidCastException || error is OverflowException)
            {
                throw new ArgumentException($"Invalid {key}: must be an integer", key, error);
            }
        }
    }
}
