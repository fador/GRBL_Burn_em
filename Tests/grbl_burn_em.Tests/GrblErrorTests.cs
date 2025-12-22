/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using Xunit;
using grbl_burn_em.Data;

namespace grbl_burn_em.Tests;

public class GrblErrorTests
{
    [Theory]
    [InlineData("1", "G-code words consist of a letter and a value. Letter was not found.")]
    [InlineData("2", "Numeric value format is not valid or missing.")]
    [InlineData("38", "Tool number greater than max supported value.")]
    [InlineData("999", "Unknown Error Code: 999")]
    [InlineData("abc", "Invalid Error Format")]
    public void TestGetMessage(string code, string expected)
    {
        string actual = GrblErrors.GetMessage(code);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("1", "Hard limit triggered. Machine position is likely lost due to sudden halt. Re-homing is highly recommended.")]
    [InlineData("9", "Homing fail. Could not find limit switch within search distance. Defined as 1.5 * max_travel on search and 5 * pulloff on locate phases.")]
    [InlineData("999", "Unknown Alarm Code: 999")]
    [InlineData("xyz", "Invalid Alarm Format")]
    public void TestGetAlarmMessage(string code, string expected)
    {
        string actual = GrblErrors.GetAlarmMessage(code);
        Assert.Equal(expected, actual);
    }
}
