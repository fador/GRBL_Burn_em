/*
 * Copyright (c) 2025 Marko Viitanen (Fador) and the project contributors. All rights reserved.
 * Licensed under the MIT License. See LICENSE file in the project root for full license information.
 *
 * This file is part of the GRBL Burn'Em laser control software
 */
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace grbl_burn_em.Data
{
    public class DirectShowDeviceInfo
    {
        public string Name { get; set; } = "";
        public string MonikerString { get; set; } = "";
    }

    // DirectShow Enumeration using P/Invoke
    public static class DeviceEnumerator
    {
        [ComImport]
        [Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86")]
        private class SystemDeviceEnum { }

        [ComImport]
        [Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICreateDevEnum
        {
            [PreserveSig]
            int CreateClassEnumerator([In] ref Guid pType, [Out] out IEnumMoniker ppEnumMoniker, [In] int dwFlags);
        }

        [ComImport]
        [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyBag
        {
            [PreserveSig]
            int Read([In, MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [In, Out, MarshalAs(UnmanagedType.Struct)] ref object pVar, [In] IntPtr pErrorLog);
            
            [PreserveSig]
            int Write([In, MarshalAs(UnmanagedType.LPWStr)] string pszPropName, [In, MarshalAs(UnmanagedType.Struct)] ref object pVar);
        }

        public static System.Collections.Generic.List<DirectShowDeviceInfo> GetAllDevices()
        {
            var result = new System.Collections.Generic.List<DirectShowDeviceInfo>();

            Guid CLSID_VideoInputDeviceCategory = new Guid("860BB310-5D01-11D0-BD3B-00A0C911CE86");
            
            ICreateDevEnum? devEnum = null;
            IEnumMoniker? enumMoniker = null;
            
            try 
            {
                Type? type = Type.GetTypeFromCLSID(new Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86"));
                if (type == null) return result;

                devEnum = (ICreateDevEnum?)Activator.CreateInstance(type);
                if (devEnum == null) return result;

                if (devEnum.CreateClassEnumerator(ref CLSID_VideoInputDeviceCategory, out enumMoniker, 0) == 0)
                {
                    if (enumMoniker == null) return result;

                    IMoniker[] monikers = new IMoniker[1];
                    IntPtr fetched = IntPtr.Zero;

                    while (enumMoniker.Next(1, monikers, fetched) == 0)
                    {
                        IMoniker moniker = monikers[0];
                        string name = "";
                        string devicePath = "";
                        
                        // Bind to storage to get the FriendlyName
                        object bagObj;
                        try
                        {
                            Guid IID_IPropertyBag = new Guid("55272A00-42CB-11CE-8135-00AA004BB851");
                             moniker.BindToStorage((IBindCtx?)null!, (IMoniker?)null!, ref IID_IPropertyBag, out bagObj);
                             
                             IPropertyBag? bag = (IPropertyBag?)bagObj;
                             if (bag != null)
                             {
                                 object val = "";
                                 bag.Read("FriendlyName", ref val, IntPtr.Zero);
                                 name = (string)val;
                             }
                        }
                        catch { }

                        // GetDisplayName (Moniker String)
                        try 
                        {
                            // moniker.GetDisplayName is generic, but IMoniker here is System.Runtime.InteropServices.ComTypes.IMoniker
                            // which has GetDisplayName method
                            moniker.GetDisplayName((IBindCtx?)null!, (IMoniker?)null!, out devicePath);
                        }
                        catch {}

                        if (!string.IsNullOrEmpty(name))
                        {
                            result.Add(new DirectShowDeviceInfo { Name = name, MonikerString = devicePath });
                        }
                        
                        Marshal.ReleaseComObject(moniker);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Device Enumeration Error: {ex.Message}");
            }
            finally
            {
                if (enumMoniker != null) Marshal.ReleaseComObject(enumMoniker);
                if (devEnum != null) Marshal.ReleaseComObject(devEnum);
            }
            
            return result;
        }
    }
}
