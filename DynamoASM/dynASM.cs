﻿//Copyright 2012 Ian Keough

//Licensed under the Apache License, Version 2.0 (the "License");
//you may not use this file except in compliance with the License.
//You may obtain a copy of the License at

//http://www.apache.org/licenses/LICENSE-2.0

//Unless required by applicable law or agreed to in writing, software
//distributed under the License is distributed on an "AS IS" BASIS,
//WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//See the License for the specific language governing permissions and
//limitations under the License.

using System;
using System.Linq;
using System.Windows.Controls;
using Dynamo.Connectors;
using Dynamo.Utilities;
using Microsoft.FSharp.Collections;
using System.Collections.Generic;

using Dynamo.FSchemeInterop;
using Value = Dynamo.FScheme.Value;

using Autodesk.ASM;
using Autodesk.DesignScript.Interfaces;

namespace Dynamo.Nodes
{
    [NodeName("ASM BSpline")]
    [NodeCategory(BuiltinNodeCategories.MISC)]
    [NodeDescription("bspline")]
    public class dynASMBSpline : dynNodeWithOneOutput
    {
        public dynASMBSpline()
        {
            //InPortData.Add(new PortData("pts", "A List of points defining the curve.", typeof(object)));
            OutPortData.Add(new PortData("bs", "The curve generated by this operation.", typeof(object)));

            NodeUI.RegisterAllPorts();
        }

        public override Value Evaluate(FSharpList<Value> args)
        {
            List<Point> pts = new List<Point>();

            pts.Add(new Point(2, 2, 0));
            pts.Add(new Point(6, 4, 0));
            pts.Add(new Point(10, 2, 0));
            pts.Add(new Point(14, -2, 0));
            pts.Add(new Point(18, 2, 0));

            BSplineCurve s1 = new BSplineCurve(pts.ToArray());
            s1.persist();
            
            //Fin
            return Value.NewContainer(s1);
        }
    }

    [NodeName("ASM Vector")]
    [NodeCategory(BuiltinNodeCategories.MISC)]
    [NodeDescription("vector")]
    public class dynASMVector : dynNodeWithOneOutput
    {
        public dynASMVector()
        {
            InPortData.Add(new PortData("x", "The x component.", typeof(object)));
            InPortData.Add(new PortData("y", "The y component.", typeof(object)));
            InPortData.Add(new PortData("z", "The z component.", typeof(object)));
            OutPortData.Add(new PortData("v", "The vector.", typeof(object)));
            NodeUI.RegisterAllPorts();
        }
        
        public override Value Evaluate(FSharpList<Value> args)
        {
            double x = (double)((Value.Number)args[0]).Item;
            double y = (double)((Value.Number)args[1]).Item;
            double z = (double)((Value.Number)args[2]).Item;

            Vector v = new Vector(x, y, z);

            //Fin
            return Value.NewContainer(v);
        }
    }

    [NodeName("ASM Surface By Extrusion")]
    [NodeCategory(BuiltinNodeCategories.MISC)]
    [NodeDescription("extrusion")]
    public class dynASMSurfaceByExtrusion : dynNodeWithOneOutput
    {
        public dynASMSurfaceByExtrusion()
        {
            InPortData.Add(new PortData("crv", "The curve to extrude.", typeof(object)));
            InPortData.Add(new PortData("d", "The distance to extrude.", typeof(object)));
            InPortData.Add(new PortData("v", "The vector along which to extrude.", typeof(object)));
            OutPortData.Add(new PortData("srf", "The surface of extrusion.", typeof(object)));
            NodeUI.RegisterAllPorts();
        }

        public override Value Evaluate(FSharpList<Value> args)
        {
            BSplineCurve s = (BSplineCurve)((Value.Container)args[0]).Item;
            double d = (double)((Value.Number)args[1]).Item;
            Vector v = (Vector)((Value.Container)args[2]).Item;

            Surface srf = s.Extrude(v, d) as Surface;
            srf.persist();

            //Fin
            return Value.NewContainer(srf);
        }
    }
}