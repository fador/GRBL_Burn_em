using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using laser_gui_test.Forms;

namespace laser_gui_test.Data.Generators
{
    public class CustomShapeParameters : ShapeParameters, ICustomTypeDescriptor
    {
        private string _definitions = "a=10; b=5";
        private Dictionary<string, double> _paramValues = new Dictionary<string, double>();
        private PropertyDescriptorCollection _globalProps;

        public CustomShapeParameters()
        {
            ParseDefinitions();
            _globalProps = TypeDescriptor.GetProperties(this, true);
        }

        [Category("Formula"), Description("Script to calculate x and y. Use t as loop variable.")]
        [Editor("System.ComponentModel.Design.MultilineStringEditor, System.Design, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a", typeof(System.Drawing.Design.UITypeEditor))]
        public string Formula { get; set; } = "x = 16 * sin(t)^3\r\ny = 13 * cos(t) - 5 * cos(2*t) - 2 * cos(3*t) - cos(4*t)";

        [Category("Loop"), Description("Step size for t")]
        public float StepSize { get; set; } = 0.1f;

        [Category("Loop"), Description("Maximum number of steps")]
        public int MaxSteps { get; set; } = 1000;

        [Category("Configuration"), Description("Define variables here (e.g. a=10; b=5). semicolon separated.")]
        public string Definitions
        {
            get => _definitions;
            set
            {
                if (_definitions != value)
                {
                    _definitions = value;
                    ParseDefinitions();
                }
            }
        }

        private void ParseDefinitions()
        {
            var newKeys = new HashSet<string>();
            if (!string.IsNullOrWhiteSpace(_definitions))
            {
                var parts = _definitions.Split(';');
                foreach (var part in parts)
                {
                    var kv = part.Split('=');
                    if (kv.Length == 2)
                    {
                        string key = kv[0].Trim();
                        if (double.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double val))
                        {
                            if (!_paramValues.ContainsKey(key))
                            {
                                _paramValues[key] = val;
                            }
                            newKeys.Add(key);
                        }
                    }
                }
            }
            
            // Remove old
            var toRemove = _paramValues.Keys.Where(k => !newKeys.Contains(k)).ToList();
            foreach (var k in toRemove) _paramValues.Remove(k);
        }

        public override List<PointF> Generate()
        {
            var points = new List<PointF>();
            var evaluator = new MathEvaluator();
            
            // Set params
            foreach (var kvp in _paramValues)
            {
                evaluator.SetVariable(kvp.Key, kvp.Value);
            }

            if (MaxSteps <= 0) return points;

            for (int i = 0; i < MaxSteps; i++)
            {
                double t = i * StepSize;
                evaluator.SetVariable("t", t);

                evaluator.Execute(Formula);
                
                double x = evaluator.GetVariable("x");
                double y = evaluator.GetVariable("y");
                
                // Only add if x/y changed or are valid?
                // Assuming script sets x and y each time.
                points.Add(new PointF((float)x, (float)y));
            }

            return points;
        }

        // ICustomTypeDescriptor implementation remains roughly the same but simplified property set
        public AttributeCollection GetAttributes() => TypeDescriptor.GetAttributes(this, true);
        public string GetClassName() => TypeDescriptor.GetClassName(this, true);
        public string GetComponentName() => TypeDescriptor.GetComponentName(this, true);
        public TypeConverter GetConverter() => TypeDescriptor.GetConverter(this, true);
        public EventDescriptor GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(this, true);
        public PropertyDescriptor GetDefaultProperty() => null;
        public object GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(this, editorBaseType, true);
        public EventDescriptorCollection GetEvents() => TypeDescriptor.GetEvents(this, true);
        public EventDescriptorCollection GetEvents(Attribute[] attributes) => TypeDescriptor.GetEvents(this, attributes, true);
        
        public PropertyDescriptorCollection GetProperties() => GetProperties(null);

        public PropertyDescriptorCollection GetProperties(Attribute[] attributes)
        {
            var props = new List<PropertyDescriptor>();
            
            // Add static properties
            foreach (PropertyDescriptor pd in TypeDescriptor.GetProperties(this, true))
            {
                props.Add(pd);
            }

            // Add dynamic properties
            foreach (var key in _paramValues.Keys)
            {
                props.Add(new DynamicPropertyDescriptor(key, _paramValues));
            }

            return new PropertyDescriptorCollection(props.ToArray());
        }

        public object GetPropertyOwner(PropertyDescriptor pd) => this;
    }

    public class DynamicPropertyDescriptor : PropertyDescriptor
    {
        private string _key;
        private Dictionary<string, double> _dict;

        public DynamicPropertyDescriptor(string key, Dictionary<string, double> dict) 
            : base(key, new Attribute[] { new CategoryAttribute("Variables") })
        {
            _key = key;
            _dict = dict;
        }

        public override Type ComponentType => typeof(CustomShapeParameters);
        public override bool IsReadOnly => false;
        public override Type PropertyType => typeof(double);
        public override bool CanResetValue(object component) => false;
        public override object GetValue(object component) => _dict[_key];
        public override void ResetValue(object component) { }
        public override void SetValue(object component, object value)
        {
            _dict[_key] = Convert.ToDouble(value);
        }
        public override bool ShouldSerializeValue(object component) => false;
    }
}
