/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System.Drawing;
using System.Drawing.Imaging;
using Emgu.CV;
using Emgu.CV.Aruco;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;

namespace grbl_burn_em.Data;

public class CharucoBoardConfig
{
    public string DictionaryName { get; set; } = "DICT_4X4_50";
    public int SquaresX { get; set; } = 5;
    public int SquaresY { get; set; } = 7;
    public float SquareLengthMm { get; set; } = 20f;
    public float MarkerLengthMm { get; set; } = 15f;

    public Dictionary GetDictionary()
    {
        return DictionaryName switch
        {
            "DICT_4X4_50" => new Dictionary(Dictionary.PredefinedDictionaryName.Dict4X4_50),
            "DICT_4X4_100" => new Dictionary(Dictionary.PredefinedDictionaryName.Dict4X4_100),
            "DICT_4X4_250" => new Dictionary(Dictionary.PredefinedDictionaryName.Dict4X4_250),
            "DICT_4X4_1000" => new Dictionary(Dictionary.PredefinedDictionaryName.Dict4X4_1000),
            "DICT_5X5_50" => new Dictionary(Dictionary.PredefinedDictionaryName.Dict5X5_50),
            "DICT_5X5_100" => new Dictionary(Dictionary.PredefinedDictionaryName.Dict5X5_100),
            "DICT_5X5_250" => new Dictionary(Dictionary.PredefinedDictionaryName.Dict5X5_250),
            "DICT_5X5_1000" => new Dictionary(Dictionary.PredefinedDictionaryName.Dict5X5_1000),
            "DICT_6X6_50" => new Dictionary(Dictionary.PredefinedDictionaryName.Dict6X6_50),
            "DICT_6X6_100" => new Dictionary(Dictionary.PredefinedDictionaryName.Dict6X6_100),
            "DICT_6X6_250" => new Dictionary(Dictionary.PredefinedDictionaryName.Dict6X6_250),
            "DICT_6X6_1000" => new Dictionary(Dictionary.PredefinedDictionaryName.Dict6X6_1000),
            "DICT_7X7_50" => new Dictionary(Dictionary.PredefinedDictionaryName.Dict7X7_50),
            "DICT_7X7_100" => new Dictionary(Dictionary.PredefinedDictionaryName.Dict7X7_100),
            "DICT_7X7_250" => new Dictionary(Dictionary.PredefinedDictionaryName.Dict7X7_250),
            "DICT_7X7_1000" => new Dictionary(Dictionary.PredefinedDictionaryName.Dict7X7_1000),
            _ => new Dictionary(Dictionary.PredefinedDictionaryName.Dict4X4_50)
        };
    }

    public CharucoBoard CreateBoard()
    {
        return new CharucoBoard(SquaresX, SquaresY, SquareLengthMm, MarkerLengthMm, GetDictionary());
    }

    public Bitmap GeneratePreviewImage(int marginPx = 50)
    {
        var board = CreateBoard();
        int imgW = SquaresX * 40 + 2 * marginPx;
        int imgH = SquaresY * 40 + 2 * marginPx;
        using var img = new Mat();
        ArucoInvoke.GenerateImage(board, new System.Drawing.Size(imgW, imgH), img, marginPx, 1);
        var result = new Bitmap(imgW, imgH, PixelFormat.Format24bppRgb);
        var data = new byte[imgW * imgH * 3];
        if (img.NumberOfChannels == 1)
        {
            var grayData = new byte[imgW * imgH];
            System.Runtime.InteropServices.Marshal.Copy(img.DataPointer, grayData, 0, grayData.Length);
            for (int i = 0; i < grayData.Length; i++)
            {
                data[i * 3] = grayData[i];
                data[i * 3 + 1] = grayData[i];
                data[i * 3 + 2] = grayData[i];
            }
        }
        var bd = result.LockBits(new Rectangle(0, 0, imgW, imgH), ImageLockMode.WriteOnly, result.PixelFormat);
        System.Runtime.InteropServices.Marshal.Copy(data, 0, bd.Scan0, data.Length);
        result.UnlockBits(bd);
        return result;
    }

    public void SavePreviewImage(string filePath, int dpi = 300)
    {
        float mmWidth = SquaresX * SquareLengthMm;
        float mmHeight = SquaresY * SquareLengthMm;
        int pxW = (int)(mmWidth * dpi / 25.4f);
        int pxH = (int)(mmHeight * dpi / 25.4f);
        int margin = pxW / 20;
        var board = CreateBoard();
        using var img = new Mat();
        ArucoInvoke.GenerateImage(board, new System.Drawing.Size(pxW + 2 * margin, pxH + 2 * margin), img, margin, 1);
        var bmp = new Bitmap(pxW + 2 * margin, pxH + 2 * margin, PixelFormat.Format24bppRgb);
        var grayData = new byte[(pxW + 2 * margin) * (pxH + 2 * margin)];
        System.Runtime.InteropServices.Marshal.Copy(img.DataPointer, grayData, 0, grayData.Length);
        var rgbData = new byte[grayData.Length * 3];
        for (int i = 0; i < grayData.Length; i++)
        {
            byte v = (byte)(255 - grayData[i]);
            rgbData[i * 3] = v;
            rgbData[i * 3 + 1] = v;
            rgbData[i * 3 + 2] = v;
        }
        var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, bmp.PixelFormat);
        System.Runtime.InteropServices.Marshal.Copy(rgbData, 0, bd.Scan0, rgbData.Length);
        bmp.UnlockBits(bd);
        bmp.SetResolution(dpi, dpi);
        bmp.Save(filePath, ImageFormat.Png);
        bmp.Dispose();
    }

    public static string[] AvailableDictionaries { get; } =
    {
        "DICT_4X4_50", "DICT_4X4_100", "DICT_4X4_250", "DICT_4X4_1000",
        "DICT_5X5_50", "DICT_5X5_100", "DICT_5X5_250", "DICT_5X5_1000",
        "DICT_6X6_50", "DICT_6X6_100", "DICT_6X6_250", "DICT_6X6_1000",
        "DICT_7X7_50", "DICT_7X7_100", "DICT_7X7_250", "DICT_7X7_1000"
    };
}
