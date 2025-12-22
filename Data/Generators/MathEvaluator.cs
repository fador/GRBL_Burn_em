/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace grbl_burn_em.Data.Generators
{
    public class MathEvaluator
    {
        private Dictionary<string, double> _variables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        public MathEvaluator()
        {
            _variables["pi"] = Math.PI;
            _variables["e"] = Math.E;
        }

        public void SetVariable(string name, double value)
        {
            _variables[name] = value;
        }

        public double GetVariable(string name)
        {
            return _variables.TryGetValue(name, out double val) ? val : 0;
        }

        public void Execute(string script)
        {
            if (string.IsNullOrWhiteSpace(script)) return;
            
            // Split by newline or semicolon
            var lines = script.Split(new[] { '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                string stmt = line.Trim();
                if (string.IsNullOrWhiteSpace(stmt) || stmt.StartsWith("//")) continue;

                // Check for assignment: var = expr
                int eqIdx = stmt.IndexOf('=');
                if (eqIdx > 0)
                {
                    string varName = stmt.Substring(0, eqIdx).Trim();
                    string expr = stmt.Substring(eqIdx + 1).Trim();
                    
                    // Validate varName
                    if (IsFunction(varName)) continue; // Can't assign to function
                    
                    double val = Evaluate(expr);
                    SetVariable(varName, val);
                }
                else
                {
                    // Naked expression? Just evaluate (maybe side effects in future?)
                    Evaluate(stmt);
                }
            }
        }

        public double Evaluate(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) return 0;
            var tokens = Tokenize(expression);
            var rpn = ToRPN(tokens);
            return EvaluateRPN(rpn);
        }

        private enum TokenType { Number, Identifier, Operator, OpenParen, CloseParen, Comma }
        private struct Token { public TokenType Type; public string Value; }

        private List<Token> Tokenize(string expr)
        {
            var tokens = new List<Token>();
            int i = 0;
            while (i < expr.Length)
            {
                char c = expr[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }

                if (char.IsDigit(c) || c == '.')
                {
                    string num = "";
                    while (i < expr.Length && (char.IsDigit(expr[i]) || expr[i] == '.'))
                        num += expr[i++];
                    tokens.Add(new Token { Type = TokenType.Number, Value = num });
                }
                else if (char.IsLetter(c))
                {
                    string id = "";
                    while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_'))
                        id += expr[i++];
                    tokens.Add(new Token { Type = TokenType.Identifier, Value = id });
                }
                else if (c == ',')
                {
                    tokens.Add(new Token { Type = TokenType.Comma, Value = "," });
                    i++;
                }
                else if (c == '-')
                {
                    // Check for unary minus
                    // Start of expr, or after operator, open paren, comma
                    bool isUnary = tokens.Count == 0 || 
                                   tokens.Last().Type == TokenType.Operator || 
                                   tokens.Last().Type == TokenType.OpenParen || 
                                   tokens.Last().Type == TokenType.Comma;
                    
                    if (isUnary)
                        tokens.Add(new Token { Type = TokenType.Operator, Value = "~" }); // Unary minus
                    else
                        tokens.Add(new Token { Type = TokenType.Operator, Value = "-" }); // Binary minus
                    i++;
                }
                else if ("+*/^%".Contains(c))
                {
                    tokens.Add(new Token { Type = TokenType.Operator, Value = c.ToString() });
                    i++;
                }
                else if (c == '(') { tokens.Add(new Token { Type = TokenType.OpenParen, Value = "(" }); i++; }
                else if (c == ')') { tokens.Add(new Token { Type = TokenType.CloseParen, Value = ")" }); i++; }
                else { i++; } // Skip unknown
            }
            return tokens;
        }

        private Queue<Token> ToRPN(List<Token> tokens)
        {
            var output = new Queue<Token>();
            var stack = new Stack<Token>();
            
            foreach (var token in tokens)
            {
                if (token.Type == TokenType.Number || token.Type == TokenType.Identifier)
                {
                    if (token.Type == TokenType.Identifier && IsFunction(token.Value))
                    {
                        stack.Push(token);
                    }
                    else
                    {
                        output.Enqueue(token);
                    }
                }
                else if (token.Type == TokenType.Comma)
                {
                    while (stack.Count > 0 && stack.Peek().Type != TokenType.OpenParen)
                        output.Enqueue(stack.Pop());
                }
                else if (token.Type == TokenType.Operator)
                {
                    while (stack.Count > 0 && stack.Peek().Type == TokenType.Operator &&
                           // Right associative for ^ and ~ (unary)
                           ( (token.Value == "^" || token.Value == "~") ? 
                             GetPrecedence(stack.Peek().Value) > GetPrecedence(token.Value) :
                             GetPrecedence(stack.Peek().Value) >= GetPrecedence(token.Value) )
                           )
                    {
                        output.Enqueue(stack.Pop());
                    }
                    stack.Push(token);
                }
                else if (token.Type == TokenType.OpenParen)
                {
                    stack.Push(token);
                }
                else if (token.Type == TokenType.CloseParen)
                {
                    while (stack.Count > 0 && stack.Peek().Type != TokenType.OpenParen)
                        output.Enqueue(stack.Pop());
                    if (stack.Count > 0 && stack.Peek().Type == TokenType.OpenParen)
                        stack.Pop(); 
                    if (stack.Count > 0 && stack.Peek().Type == TokenType.Identifier && IsFunction(stack.Peek().Value))
                        output.Enqueue(stack.Pop()); 
                }
            }

            while (stack.Count > 0)
                output.Enqueue(stack.Pop());

            return output;
        }

        private double EvaluateRPN(Queue<Token> tokens)
        {
            var stack = new Stack<double>();

            foreach (var token in tokens)
            {
                if (token.Type == TokenType.Number)
                {
                    stack.Push(double.Parse(token.Value, CultureInfo.InvariantCulture));
                }
                else if (token.Type == TokenType.Identifier)
                {
                    if (IsFunction(token.Value))
                    {
                        ApplyFunction(token.Value, stack);
                    }
                    else
                    {
                        if (_variables.TryGetValue(token.Value, out double val))
                            stack.Push(val);
                        else
                            stack.Push(0); 
                    }
                }
                else if (token.Type == TokenType.Operator)
                {
                    if (token.Value == "~")
                    {
                         if (stack.Count < 1) return 0;
                         double a = stack.Pop();
                         stack.Push(-a);
                    }
                    else
                    {
                        if (stack.Count < 2) return 0; 
                        double b = stack.Pop();
                        double a = stack.Pop();
                        stack.Push(ApplyOp(token.Value, a, b));
                    }
                }
            }

            return stack.Count > 0 ? stack.Pop() : 0;
        }

        private bool IsFunction(string name)
        {
            return new[] { "sin", "cos", "tan", "sqrt", "abs", "floor", "ceil", "min", "max", "pow", "log" }.Contains(name.ToLower());
        }

        private int GetPrecedence(string op)
        {
            if (op == "~") return 5; // Unary minus high precedence
            if (op == "^") return 4;
            if (op == "*" || op == "/" || op == "%") return 3;
            if (op == "+" || op == "-") return 2;
            return 0;
        }

        private double ApplyOp(string op, double a, double b)
        {
            switch (op)
            {
                case "+": return a + b;
                case "-": return a - b;
                case "*": return a * b;
                case "/": return b == 0 ? 0 : a / b;
                case "%": return a % b;
                case "^": return Math.Pow(a, b);
                default: return 0;
            }
        }

        private void ApplyFunction(string func, Stack<double> stack)
        {
            // Some functions take 2 args
            if (func.ToLower() == "pow" || func.ToLower() == "min" || func.ToLower() == "max") 
            {
                if (stack.Count < 2) { stack.Push(0); return; }
                double b = stack.Pop();
                double a = stack.Pop();
                if (func.ToLower() == "pow") stack.Push(Math.Pow(a, b));
                if (func.ToLower() == "min") stack.Push(Math.Min(a, b));
                if (func.ToLower() == "max") stack.Push(Math.Max(a, b));
                return;
            }

            if (stack.Count < 1) { stack.Push(0); return; }
            double v = stack.Pop();
            switch (func.ToLower())
            {
                case "sin": stack.Push(Math.Sin(v)); break;
                case "cos": stack.Push(Math.Cos(v)); break;
                case "tan": stack.Push(Math.Tan(v)); break;
                case "sqrt": stack.Push(Math.Sqrt(v)); break;
                case "abs": stack.Push(Math.Abs(v)); break;
                case "floor": stack.Push(Math.Floor(v)); break;
                case "ceil": stack.Push(Math.Ceiling(v)); break;
                case "log": stack.Push(Math.Log(v)); break;
            }
        }
    }
}
