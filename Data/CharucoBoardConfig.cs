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
            for (int y = 0; y < imgH; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    img.DataPointer + y * img.Step, grayData, y * imgW, imgW);
            }
            for (int i = 0; i < grayData.Length; i++)
            {
                data[i * 3] = grayData[i];
                data[i * 3 + 1] = grayData[i];
                data[i * 3 + 2] = grayData[i];
            }
        }
        var bd = result.LockBits(new Rectangle(0, 0, imgW, imgH), ImageLockMode.WriteOnly, result.PixelFormat);
        for (int y = 0; y < imgH; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(
                data, y * imgW * 3, bd.Scan0 + y * bd.Stride, imgW * 3);
        }
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
        int imgW = img.Width, imgH = img.Height;
        var grayData = new byte[imgW * imgH];
        // Copy row-by-row: OpenCV rows may be padded to a larger stride than the width.
        for (int y = 0; y < imgH; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(
                img.DataPointer + y * img.Step, grayData, y * imgW, imgW);
        }
        var rgbData = new byte[grayData.Length * 3];
        for (int i = 0; i < grayData.Length; i++)
        {
            byte v = grayData[i];
            rgbData[i * 3] = v;
            rgbData[i * 3 + 1] = v;
            rgbData[i * 3 + 2] = v;
        }
        var bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.WriteOnly, bmp.PixelFormat);
        // Copy row-by-row: the bitmap stride may be padded beyond 3 * imgW bytes.
        for (int y = 0; y < imgH; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(
                rgbData, y * imgW * 3, bd.Scan0 + y * bd.Stride, imgW * 3);
        }
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
