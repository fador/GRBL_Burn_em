/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using Xunit;
using grbl_burn_em.Data.Generators;

namespace grbl_burn_em.Tests
{
    public class MathEvaluatorTests
    {
        private MathEvaluator _evaluator;

        public MathEvaluatorTests()
        {
            _evaluator = new MathEvaluator();
        }

        [Fact]
        public void TestBasicArithmetic()
        {
            Assert.Equal(5, _evaluator.Evaluate("2 + 3"), 3);
            Assert.Equal(-1, _evaluator.Evaluate("2 - 3"), 3);
            Assert.Equal(6, _evaluator.Evaluate("2 * 3"), 3);
            Assert.Equal(2.5, _evaluator.Evaluate("5 / 2"), 3);
            Assert.Equal(1, _evaluator.Evaluate("5 % 2"), 3);
            Assert.Equal(8, _evaluator.Evaluate("2 ^ 3"), 3);
        }

        [Fact]
        public void TestFunctions()
        {
            Assert.Equal(0, _evaluator.Evaluate("sin(0)"), 3);
            Assert.Equal(1, _evaluator.Evaluate("cos(0)"), 3);
            Assert.Equal(0, _evaluator.Evaluate("tan(0)"), 3);
            Assert.Equal(4, _evaluator.Evaluate("sqrt(16)"), 3);
            Assert.Equal(5, _evaluator.Evaluate("abs(-5)"), 3);
            Assert.Equal(5, _evaluator.Evaluate("floor(5.9)"), 3);
            Assert.Equal(6, _evaluator.Evaluate("ceil(5.1)"), 3);
            Assert.Equal(1, _evaluator.Evaluate("log(e)"), 3); // ln(e) = 1
        }

        [Fact]
        public void TestMultiArgFunctions()
        {
            Assert.Equal(2, _evaluator.Evaluate("min(2, 5)"), 3);
            Assert.Equal(5, _evaluator.Evaluate("max(2, 5)"), 3);
            Assert.Equal(8, _evaluator.Evaluate("pow(2, 3)"), 3);
        }

        [Fact]
        public void TestVariables()
        {
            _evaluator.SetVariable("x", 10);
            Assert.Equal(20, _evaluator.Evaluate("x * 2"), 3);
        }

        [Fact]
        public void TestScriptExecution()
        {
            string script = @"
                a = 5
                b = 10
                c = a + b
                result = pow(c, 2)
            ";
            _evaluator.Execute(script);
            Assert.Equal(5, _evaluator.GetVariable("a"), 3);
            Assert.Equal(10, _evaluator.GetVariable("b"), 3);
            Assert.Equal(15, _evaluator.GetVariable("c"), 3);
            Assert.Equal(225, _evaluator.GetVariable("result"), 3);
        }

        [Fact]
        public void TestComplexFormula()
        {
            // (2 + 3) * 4 = 20
            Assert.Equal(20, _evaluator.Evaluate("(2 + 3) * 4"), 3);
            
            // Nested functions
            // sqrt(pow(3, 2) + pow(4, 2)) = sqrt(9 + 16) = sqrt(25) = 5
            Assert.Equal(5, _evaluator.Evaluate("sqrt(pow(3, 2) + pow(4, 2))"), 3);
            
            // Inlined multi-arg
            Assert.Equal(8, _evaluator.Evaluate("pow(sqrt(4), 3)"), 3);
        }

        [Fact]
        public void TestConstants()
        {
            Assert.Equal(Math.PI, _evaluator.Evaluate("pi"), 3);
            Assert.Equal(Math.E, _evaluator.Evaluate("e"), 3);
        }
    }
}
