using NUnit.Framework;
using System;
using System.Collections.Generic;
using X02Competition.Protocol;

namespace X02Competition.Tests.Editor
{
    /// <summary>
    /// InputValidator 工具类单元测试。
    /// </summary>
    public class InputValidatorTests
    {
        [Test]
        public void TestClampFloat_Valid()
        {
            var result = InputValidator.ClampFloat(5.0f, 0f, 10f, "test");
            Assert.AreEqual(5.0f, result);
        }

        [Test]
        public void TestClampFloat_ClampMin()
        {
            var result = InputValidator.ClampFloat(-5.0f, 0f, 10f, "test");
            Assert.AreEqual(0f, result);
        }

        [Test]
        public void TestClampFloat_ClampMax()
        {
            var result = InputValidator.ClampFloat(15.0f, 0f, 10f, "test");
            Assert.AreEqual(10f, result);
        }

        [Test]
        public void TestClampFloat_NaN()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                InputValidator.ClampFloat(float.NaN, 0f, 10f, "test");
            });
        }

        [Test]
        public void TestClampFloat_Infinity()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                InputValidator.ClampFloat(float.PositiveInfinity, 0f, 10f, "test");
            });
        }

        [Test]
        public void TestClampInt_Valid()
        {
            var result = InputValidator.ClampInt(5, 0, 10, "test");
            Assert.AreEqual(5, result);
        }

        [Test]
        public void TestClampInt_OutOfRangeMin()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                InputValidator.ClampInt(-5, 0, 10, "test");
            });
        }

        [Test]
        public void TestClampInt_OutOfRangeMax()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                InputValidator.ClampInt(15, 0, 10, "test");
            });
        }

        [Test]
        public void TestRequireNonEmpty_Valid()
        {
            var result = InputValidator.RequireNonEmpty("hello", "test");
            Assert.AreEqual("hello", result);
        }

        [Test]
        public void TestRequireNonEmpty_Null()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                InputValidator.RequireNonEmpty(null, "test");
            });
        }

        [Test]
        public void TestRequireNonEmpty_Empty()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                InputValidator.RequireNonEmpty("", "test");
            });
        }

        [Test]
        public void TestValidateStringLength_Valid()
        {
            var result = InputValidator.ValidateStringLength("hello", 1, 10, "test");
            Assert.AreEqual("hello", result);
        }

        [Test]
        public void TestValidateStringLength_TooShort()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                InputValidator.ValidateStringLength("hi", 5, 10, "test");
            });
        }

        [Test]
        public void TestValidateStringLength_TooLong()
        {
            Assert.Throws<ArgumentException>(() =>
            {
                InputValidator.ValidateStringLength("hello world!", 1, 5, "test");
            });
        }

        [Test]
        public void TestGetFloatParam_Exists()
        {
            var dict = new Dictionary<string, object>
            {
                { "distance", 2.5f }
            };
            var result = InputValidator.GetFloatParam(dict, "distance", 1.0f, 0f, 5f);
            Assert.AreEqual(2.5f, result);
        }

        [Test]
        public void TestGetFloatParam_Missing()
        {
            var dict = new Dictionary<string, object>();
            var result = InputValidator.GetFloatParam(dict, "distance", 1.0f, 0f, 5f);
            Assert.AreEqual(1.0f, result);
        }

        [Test]
        public void TestGetFloatParam_Null()
        {
            var result = InputValidator.GetFloatParam(null, "distance", 1.0f, 0f, 5f);
            Assert.AreEqual(1.0f, result);
        }

        [Test]
        public void TestGetFloatParam_ClampMin()
        {
            var dict = new Dictionary<string, object>
            {
                { "distance", -5.0f }
            };
            var result = InputValidator.GetFloatParam(dict, "distance", 1.0f, 0f, 5f);
            Assert.AreEqual(0f, result);
        }

        [Test]
        public void TestGetFloatParam_ClampMax()
        {
            var dict = new Dictionary<string, object>
            {
                { "distance", 10.0f }
            };
            var result = InputValidator.GetFloatParam(dict, "distance", 1.0f, 0f, 5f);
            Assert.AreEqual(5f, result);
        }

        [Test]
        public void TestGetFloatParam_TypeConversion()
        {
            var dict = new Dictionary<string, object>
            {
                { "distance", 2 }  // int to float
            };
            var result = InputValidator.GetFloatParam(dict, "distance", 1.0f, 0f, 5f);
            Assert.AreEqual(2.0f, result);
        }

        [Test]
        public void TestGetFloatParam_InvalidType()
        {
            var dict = new Dictionary<string, object>
            {
                { "distance", "not a number" }
            };
            Assert.Throws<ArgumentException>(() =>
                InputValidator.GetFloatParam(dict, "distance", 1.0f, 0f, 5f));
        }

        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("-Infinity")]
        [TestCase(true)]
        [TestCase(null)]
        public void ExplicitInvalidMotionValueIsNotReplacedByDefault(object value)
        {
            var parameters = new Dictionary<string, object> { ["distanceM"] = value };
            Assert.Throws<ArgumentException>(() =>
                InputValidator.GetFloatParam(parameters, "distanceM", 1f, 0.2f, 5f));
        }

        [Test]
        public void FloatStringsUseProtocolCulture()
        {
            var original = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    new System.Globalization.CultureInfo("fr-FR");
                var parameters = new Dictionary<string, object> { ["distanceM"] = "1.5" };
                Assert.AreEqual(1.5f, InputValidator.GetFloatParam(parameters, "distanceM", 1f, 0.2f, 5f));
            }
            finally { System.Threading.Thread.CurrentThread.CurrentCulture = original; }
        }

        [TestCase("NaN")]
        [TestCase("not a number")]
        [TestCase(true)]
        [TestCase(null)]
        public void InvalidIntegerIsRejected(object value)
        {
            var parameters = new Dictionary<string, object> { ["duration"] = value };
            Assert.Throws<ArgumentException>(() =>
                InputValidator.GetIntParam(parameters, "duration", 3000, 1000, 10000));
        }

        [Test]
        public void TestGetIntParam_Exists()
        {
            var dict = new Dictionary<string, object>
            {
                { "duration", 3000 }
            };
            var result = InputValidator.GetIntParam(dict, "duration", 1000, 1000, 10000);
            Assert.AreEqual(3000, result);
        }

        [Test]
        public void TestGetIntParam_Missing()
        {
            var dict = new Dictionary<string, object>();
            var result = InputValidator.GetIntParam(dict, "duration", 1000, 1000, 10000);
            Assert.AreEqual(1000, result);
        }

        [Test]
        public void TestGetIntParam_ClampMin()
        {
            var dict = new Dictionary<string, object>
            {
                { "duration", 500 }
            };
            var result = InputValidator.GetIntParam(dict, "duration", 1000, 1000, 10000);
            Assert.AreEqual(1000, result);
        }

        [Test]
        public void TestGetIntParam_ClampMax()
        {
            var dict = new Dictionary<string, object>
            {
                { "duration", 15000 }
            };
            var result = InputValidator.GetIntParam(dict, "duration", 1000, 1000, 10000);
            Assert.AreEqual(10000, result);
        }

        [Test]
        public void TestGetIntParam_TypeConversion()
        {
            var dict = new Dictionary<string, object>
            {
                { "duration", 3000.5f }  // float to int
            };
            var result = InputValidator.GetIntParam(dict, "duration", 1000, 1000, 10000);
            Assert.AreEqual(3000, result);
        }
    }
}
