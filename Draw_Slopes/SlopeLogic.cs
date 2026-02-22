using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.GraphicsInterface;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcColor = Autodesk.AutoCAD.Colors.Color;

using DbPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace Draw_Slopes
{
    public struct AdjustedRange
    {
        public double StartCrest;
        public double EndCrest;
        public double StartToe;
        public double EndToe;
    }

    public class SlopeLogic
    {
        private static List<ObjectId> _previewLineIds = new List<ObjectId>();

        [CommandMethod("DRAWSLOPE")]
        public void RunSlopeCommand()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            ClearPreview(doc);

            while (true)
            {
                SlopeWindow window = new SlopeWindow();
                bool? result = AcApp.ShowModalWindow(window);

                if (window.IsDeleteRequest)
                {
                    DeleteSelectedObjects(doc);
                    doc.Editor.UpdateScreen();
                    doc.Editor.Regen();
                    System.Windows.Forms.Application.DoEvents();
                    continue;
                }

                if (result != true) break;

                PromptEntityOptions opt1 = new PromptEntityOptions("\nSelect Top Line (Crest): ");
                opt1.SetRejectMessage("\nOnly Curve type objects allowed!");
                opt1.AddAllowedClass(typeof(Curve), false);

                PromptEntityResult res1 = ed.GetEntity(opt1);
                if (res1.Status != PromptStatus.OK) continue;

                HighlightEntity(res1.ObjectId, true);

                PromptEntityOptions opt2 = new PromptEntityOptions("\nSelect Bottom Line (Toe): ");
                opt2.SetRejectMessage("\nOnly Curve type objects allowed!");
                opt2.AddAllowedClass(typeof(Curve), false);

                PromptEntityResult res2 = ed.GetEntity(opt2);
                if (res2.Status != PromptStatus.OK)
                {
                    HighlightEntity(res1.ObjectId, false);
                    continue;
                }

                HighlightEntity(res2.ObjectId, true);

                double step = window.Step;
                double offset = window.Offset;
                double ratio = window.Ratio > 1 ? window.Ratio / 100.0 : window.Ratio;
                int colorIdx = window.SelectedColorIndex;
                string layerName = window.LayerName;
                bool isGroup = window.IsGroup;
                bool isBackground = window.IsBackground;
                int backColorIdx = window.BackgroundHatchColorIndex;

                string strokeStyle = window.StrokeStyle;
                bool isTriangle = window.IsTriangle;
                string curtainType = window.CurtainType; // >>> ვიღებთ ახალ პარამეტრს <<<

                string method = window.Method;
                bool autoOrient = window.AutoOrient;
                bool is3D = window.Is3D;

                double globalStartDist = 0;
                double globalEndDist = 0;
                List<AdjustedRange> manualAdjustments = new List<AdjustedRange>();

                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    Curve? crest = tr.GetObject(res1.ObjectId, OpenMode.ForRead) as Curve;
                    if (crest != null)
                    {
                        using (Curve crestProj = crest.GetProjectedCurve(new Plane(Point3d.Origin, Vector3d.ZAxis), Vector3d.ZAxis) as Curve)
                        {
                            globalEndDist = crestProj.GetDistanceAtParameter(crestProj.EndParam);
                        }
                    }
                    tr.Commit();
                }

                PromptKeywordOptions pko = new PromptKeywordOptions("\nDraw Mode [Entire/Segment]: ");
                pko.Keywords.Add("Entire");
                pko.Keywords.Add("Segment");
                pko.Keywords.Default = "Entire";
                pko.AllowNone = true;

                PromptResult pRes = ed.GetKeywords(pko);
                if (pRes.Status != PromptStatus.OK && pRes.Status != PromptStatus.None)
                {
                    HighlightEntity(res1.ObjectId, false);
                    HighlightEntity(res2.ObjectId, false);
                    continue;
                }

                if (pRes.Status == PromptStatus.OK && pRes.StringResult == "Segment")
                {
                    PromptPointOptions ppoStart = new PromptPointOptions("\nSelect Start Point on Crest: ");
                    ppoStart.AllowNone = false;
                    PromptPointResult pprStart = ed.GetPoint(ppoStart);
                    if (pprStart.Status != PromptStatus.OK)
                    {
                        HighlightEntity(res1.ObjectId, false);
                        HighlightEntity(res2.ObjectId, false);
                        continue;
                    }

                    using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        Curve? crest = tr.GetObject(res1.ObjectId, OpenMode.ForRead) as Curve;
                        Curve? toe = tr.GetObject(res2.ObjectId, OpenMode.ForRead) as Curve;

                        if (crest != null && toe != null)
                        {
                            using (Curve crestProj = crest.GetProjectedCurve(new Plane(Point3d.Origin, Vector3d.ZAxis), Vector3d.ZAxis) as Curve)
                            {
                                Point3d startPt = crestProj.GetClosestPointTo(pprStart.Value, false);
                                globalStartDist = crestProj.GetDistAtPoint(startPt);

                                try
                                {
                                    var selectionJig = new AreaSelectJig(crest, toe, pprStart.Value, step, method, is3D);
                                    PromptResult jigRes = ed.Drag(selectionJig);

                                    if (jigRes.Status == PromptStatus.OK)
                                    {
                                        Point3d endPt = crestProj.GetClosestPointTo(selectionJig.CurrentMousePoint, false);
                                        globalEndDist = crestProj.GetDistAtPoint(endPt);

                                        if (globalStartDist > globalEndDist)
                                        {
                                            double temp = globalStartDist;
                                            globalStartDist = globalEndDist;
                                            globalEndDist = temp;
                                        }
                                    }
                                    else
                                    {
                                        HighlightEntity(res1.ObjectId, false);
                                        HighlightEntity(res2.ObjectId, false);
                                        tr.Commit();
                                        continue;
                                    }
                                }
                                catch { }
                            }
                        }
                        tr.Commit();
                    }
                }

                if (method == "Elastic")
                {
                    while (true)
                    {
                        ShowLivePreview(doc, res1.ObjectId, res2.ObjectId, step, ratio, manualAdjustments, globalStartDist, globalEndDist);
                        ed.UpdateScreen();

                        var peo1 = new PromptEntityOptions("\nSelect Start preview line for adjustment or press ENTER: ");
                        peo1.SetRejectMessage("\nPlease select a valid Line.");
                        peo1.AddAllowedClass(typeof(Line), true);
                        peo1.AppendKeywordsToMessage = true;
                        peo1.AllowNone = true;

                        var per1 = ed.GetEntity(peo1);
                        if (per1.Status != PromptStatus.OK) break;

                        ObjectId startLineId = per1.ObjectId;

                        if (!_previewLineIds.Contains(startLineId))
                        {
                            ed.WriteMessage("\nPlease select one of the generated preview lines.");
                            continue;
                        }

                        HighlightEntity(startLineId, true);

                        Point3d startPt3D;
                        double clickedDist2D = 0;
                        double totalLenCrest2D = 0;
                        double totalLenToe2D = 0;

                        using (Transaction tempTr = doc.Database.TransactionManager.StartTransaction())
                        {
                            Line selLine = (Line)tempTr.GetObject(startLineId, OpenMode.ForRead);
                            startPt3D = selLine.StartPoint;

                            Curve crest = (Curve)tempTr.GetObject(res1.ObjectId, OpenMode.ForRead);
                            Curve toe = (Curve)tempTr.GetObject(res2.ObjectId, OpenMode.ForRead);

                            using (Curve crestProj = crest.GetProjectedCurve(new Plane(Point3d.Origin, Vector3d.ZAxis), Vector3d.ZAxis) as Curve)
                            using (Curve toeProj = toe.GetProjectedCurve(new Plane(Point3d.Origin, Vector3d.ZAxis), Vector3d.ZAxis) as Curve)
                            {
                                clickedDist2D = crestProj.GetDistAtPoint(crestProj.GetClosestPointTo(startPt3D, false));
                                totalLenCrest2D = crestProj.GetDistanceAtParameter(crestProj.EndParam);
                                totalLenToe2D = toeProj.GetDistanceAtParameter(toeProj.EndParam);
                            }
                            tempTr.Commit();
                        }

                        if (clickedDist2D < globalStartDist - 0.001 || clickedDist2D > globalEndDist + 0.001)
                        {
                            ed.WriteMessage("\nPlease select a point within the defined drawing range.");
                            HighlightEntity(startLineId, false);
                            continue;
                        }

                        var peo2 = new PromptEntityOptions("\nSelect End preview line: ");
                        peo2.SetRejectMessage("\nPlease select a valid Line.");
                        peo2.AddAllowedClass(typeof(Line), true);

                        var per2 = ed.GetEntity(peo2);
                        if (per2.Status != PromptStatus.OK)
                        {
                            HighlightEntity(startLineId, false);
                            break;
                        }

                        ObjectId endLineId = per2.ObjectId;

                        if (!_previewLineIds.Contains(endLineId))
                        {
                            ed.WriteMessage("\nPlease select one of the generated preview lines.");
                            HighlightEntity(startLineId, false);
                            continue;
                        }

                        HighlightEntity(endLineId, true);

                        Point3d endPt3D;
                        using (Transaction tempTr = doc.Database.TransactionManager.StartTransaction())
                        {
                            Line selLine = (Line)tempTr.GetObject(endLineId, OpenMode.ForRead);
                            endPt3D = selLine.StartPoint;

                            Curve crest = (Curve)tempTr.GetObject(res1.ObjectId, OpenMode.ForRead);
                            Curve toe = (Curve)tempTr.GetObject(res2.ObjectId, OpenMode.ForRead);

                            double clickedEndDist2D;
                            bool toeClosed = false;
                            bool crestClosed = false;

                            using (Curve crestProj = crest.GetProjectedCurve(new Plane(Point3d.Origin, Vector3d.ZAxis), Vector3d.ZAxis) as Curve)
                            using (Curve toeProj = toe.GetProjectedCurve(new Plane(Point3d.Origin, Vector3d.ZAxis), Vector3d.ZAxis) as Curve)
                            {
                                clickedEndDist2D = crestProj.GetDistAtPoint(crestProj.GetClosestPointTo(endPt3D, false));
                                toeClosed = toeProj.Closed || toeProj.StartPoint.DistanceTo(toeProj.EndPoint) < 0.01;
                                crestClosed = crestProj.Closed || crestProj.StartPoint.DistanceTo(crestProj.EndPoint) < 0.01;

                                bool crossesSeam = false;

                                if (Math.Abs(clickedDist2D - clickedEndDist2D) > totalLenCrest2D / 2.0)
                                {
                                    if (!crestClosed)
                                    {
                                        ed.WriteMessage("\n\n[!] WARNING: Cannot bridge the gap because the Crest line is OPEN.");
                                        ed.WriteMessage("\nPlease edit this section in two separate steps, or close the Crest polyline.\n");
                                        HighlightEntity(startLineId, false);
                                        HighlightEntity(endLineId, false);
                                        tempTr.Commit();
                                        continue;
                                    }
                                    crossesSeam = true;
                                    if (clickedDist2D < clickedEndDist2D)
                                    {
                                        double temp = clickedDist2D; clickedDist2D = clickedEndDist2D; clickedEndDist2D = temp;
                                    }
                                }

                                var jig = new SlopeRangeJig(crestProj, toeProj, crest, toe, clickedDist2D, clickedEndDist2D, step, crossesSeam, totalLenCrest2D, totalLenToe2D, toeClosed);

                                if (ed.Drag(jig).Status == PromptStatus.OK)
                                {
                                    double sCrest2D = clickedDist2D;
                                    double eCrest2D = clickedEndDist2D;

                                    Point3d sCrestPtProj = crestProj.GetPointAtDist(sCrest2D);
                                    double sToe2D = toeProj.GetDistAtPoint(toeProj.GetClosestPointTo(sCrestPtProj, false));

                                    Point3d eMouseProj = toeProj.GetClosestPointTo(jig.CurrentMousePt3D, false);
                                    double eToe2D = toeProj.GetDistAtPoint(eMouseProj);

                                    if (!toeClosed && Math.Abs(eToe2D - sToe2D) > totalLenToe2D / 2.0)
                                    {
                                        ed.WriteMessage("\n\n[!] WARNING: You crossed the gap of an OPEN Bottom (Toe) line.");
                                        ed.WriteMessage("\nAdjustment canceled. Please edit up to the gap, not across it.\n");
                                        HighlightEntity(startLineId, false);
                                        HighlightEntity(endLineId, false);
                                        tempTr.Commit();
                                        continue;
                                    }

                                    double travelToe = eToe2D - sToe2D;
                                    if (toeClosed)
                                    {
                                        if (travelToe > totalLenToe2D / 2.0) travelToe -= totalLenToe2D;
                                        else if (travelToe < -totalLenToe2D / 2.0) travelToe += totalLenToe2D;
                                    }

                                    double mathEndToe = sToe2D + travelToe;

                                    if (crossesSeam)
                                    {
                                        double travelCrest;
                                        double midToe;

                                        if (sCrest2D < eCrest2D)
                                        {
                                            travelCrest = -sCrest2D - (totalLenCrest2D - eCrest2D);
                                            midToe = sToe2D + (-sCrest2D / travelCrest) * travelToe;

                                            AddAdjustment(manualAdjustments, new AdjustedRange { StartCrest = sCrest2D, EndCrest = 0, StartToe = sToe2D, EndToe = midToe });
                                            AddAdjustment(manualAdjustments, new AdjustedRange { StartCrest = totalLenCrest2D, EndCrest = eCrest2D, StartToe = midToe, EndToe = mathEndToe });
                                        }
                                        else
                                        {
                                            travelCrest = (totalLenCrest2D - sCrest2D) + eCrest2D;
                                            midToe = sToe2D + ((totalLenCrest2D - sCrest2D) / travelCrest) * travelToe;

                                            AddAdjustment(manualAdjustments, new AdjustedRange { StartCrest = sCrest2D, EndCrest = totalLenCrest2D, StartToe = sToe2D, EndToe = midToe });
                                            AddAdjustment(manualAdjustments, new AdjustedRange { StartCrest = 0, EndCrest = eCrest2D, StartToe = midToe, EndToe = mathEndToe });
                                        }
                                    }
                                    else
                                    {
                                        AddAdjustment(manualAdjustments, new AdjustedRange { StartCrest = sCrest2D, EndCrest = eCrest2D, StartToe = sToe2D, EndToe = mathEndToe });
                                    }
                                }
                            }
                            tempTr.Commit();

                            HighlightEntity(startLineId, false);
                            HighlightEntity(endLineId, false);
                        }
                    }
                    ClearPreview(doc);
                }

                DrawSlopeHatch(
                    res1.ObjectId,
                    res2.ObjectId,
                    step,
                    ratio,
                    colorIdx,
                    isGroup,
                    layerName,
                    isBackground,
                    backColorIdx,
                    offset,
                    strokeStyle,
                    isTriangle,
                    method,
                    autoOrient,
                    curtainType, // >>> ვაწვდით Curtain სტილს <<<
                    is3D,
                    manualAdjustments,
                    globalStartDist,
                    globalEndDist
                );

                HighlightEntity(res1.ObjectId, false);
                HighlightEntity(res2.ObjectId, false);
                ed.UpdateScreen();
                ed.Regen();
            }
        }

        private void AddAdjustment(List<AdjustedRange> list, AdjustedRange newAdj)
        {
            var newList = new List<AdjustedRange>();
            double newMinC = Math.Min(newAdj.StartCrest, newAdj.EndCrest);
            double newMaxC = Math.Max(newAdj.StartCrest, newAdj.EndCrest);

            foreach (var old in list)
            {
                double oldMinC = Math.Min(old.StartCrest, old.EndCrest);
                double oldMaxC = Math.Max(old.StartCrest, old.EndCrest);

                if (oldMaxC <= newMinC + 0.001 || oldMinC >= newMaxC - 0.001)
                {
                    newList.Add(old);
                    continue;
                }

                double oldCrestRng = old.EndCrest - old.StartCrest;
                double oldToeRng = old.EndToe - old.StartToe;

                if (oldMinC < newMinC)
                {
                    double splitCrest = newMinC;
                    double t = (Math.Abs(oldCrestRng) < 0.001) ? 0 : (splitCrest - old.StartCrest) / oldCrestRng;
                    double splitToe = old.StartToe + t * oldToeRng;

                    newList.Add(new AdjustedRange { StartCrest = old.StartCrest, EndCrest = splitCrest, StartToe = old.StartToe, EndToe = splitToe });
                }
                if (oldMaxC > newMaxC)
                {
                    double splitCrest = newMaxC;
                    double t = (Math.Abs(oldCrestRng) < 0.001) ? 0 : (splitCrest - old.StartCrest) / oldCrestRng;
                    double splitToe = old.StartToe + t * oldToeRng;

                    newList.Add(new AdjustedRange { StartCrest = splitCrest, EndCrest = old.EndCrest, StartToe = splitToe, EndToe = old.EndToe });
                }
            }
            newList.Add(newAdj);
            list.Clear();
            list.AddRange(newList);
        }

        public void DrawSlopeHatch(ObjectId crestId, ObjectId toeId, double step, double ratio, int colorIndex, bool createGroup, string layerName, bool hideBackground, int backColorIdx, double offset, string style, bool isTriangle, string selectedMethod, bool autoOrient, string curtainType, bool is3D, List<AdjustedRange>? manualAdjs, double startDist, double endDist)
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            Database db = doc.Database;

            if (!string.IsNullOrEmpty(layerName)) EnsureLayerExists(db, layerName, colorIndex);

            using (DocumentLock loc = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                Curve? crest = tr.GetObject(crestId, OpenMode.ForRead) as Curve;
                Curve? toe = tr.GetObject(toeId, OpenMode.ForRead) as Curve;

                if (crest == null || toe == null) return;

                using (Curve crestProj = crest.GetProjectedCurve(new Plane(Point3d.Origin, Vector3d.ZAxis), Vector3d.ZAxis) as Curve)
                using (Curve toeProj = toe.GetProjectedCurve(new Plane(Point3d.Origin, Vector3d.ZAxis), Vector3d.ZAxis) as Curve)
                {
                    if (crestProj != null && toeProj != null)
                    {
                        ObjectIdCollection idsForGroup = new ObjectIdCollection();
                        double crestLenProj = crestProj.GetDistanceAtParameter(crestProj.EndParam);
                        double toeLenProj = toeProj.GetDistanceAtParameter(toeProj.EndParam);

                        bool reverseToe = IsOppositeDirection(crestProj, toeProj);

                        if (endDist > crestLenProj) endDist = crestLenProj;
                        if (startDist < 0) startDist = 0;

                        int lineIndex = 0;

                        for (double d = startDist; d <= endDist; d += step)
                        {
                            double dSafe = Math.Min(d, crestLenProj - 1e-6);
                            double ratio2D = (Math.Abs(crestLenProj) < 0.001) ? 0 : dSafe / crestLenProj;

                            bool dummy;
                            Point3d pToe = CalculateToePointImproved(dSafe, crest, toe, crestProj, toeProj, ratio2D, selectedMethod, manualAdjs, out dummy, reverseToe);

                            double paramCrest = crestProj.GetParameterAtDistance(dSafe);
                            Point3d pCrest3D = crest.GetPointAtParameter(paramCrest);

                            Point3d sPt = is3D ? pCrest3D : new Point3d(pCrest3D.X, pCrest3D.Y, 0);
                            Point3d ePt = is3D ? pToe : new Point3d(pToe.X, pToe.Y, 0);

                            if (autoOrient && sPt.Z < ePt.Z) { var temp = sPt; sPt = ePt; ePt = temp; }

                            Vector3d fullVec = ePt - sPt;
                            if (fullVec.Length < 0.001) continue;

                            bool isShort = false;
                            if (style == "Long / 1 Short") isShort = (lineIndex % 2 != 0);
                            else if (style == "Long / 2 Shorts") isShort = (lineIndex % 3 != 0);

                            double drawLen = fullVec.Length - offset;
                            if (style != "Fixed" && isShort) drawLen *= ratio;
                            drawLen = Math.Max(0, drawLen);

                            if (drawLen > 0.01)
                            {
                                Vector3d dir = fullVec.GetNormal();
                                Point3d endPoint = sPt + (dir * drawLen);

                                if (isTriangle && !isShort)
                                {
                                    Vector3d perp = dir.CrossProduct(Vector3d.ZAxis).GetNormal();
                                    double halfWidth = step * 0.25;

                                    Point3d p1 = sPt + (perp * halfWidth);
                                    Point3d p2 = sPt - (perp * halfWidth);

                                    DbPolyline tri = new DbPolyline();
                                    tri.AddVertexAt(0, new Point2d(p1.X, p1.Y), 0, 0, 0);
                                    tri.AddVertexAt(1, new Point2d(p2.X, p2.Y), 0, 0, 0);
                                    tri.AddVertexAt(2, new Point2d(endPoint.X, endPoint.Y), 0, 0, 0);
                                    tri.Closed = true;

                                    tri.ColorIndex = colorIndex;
                                    SafeSetLayer(tri, layerName);

                                    btr.AppendEntity(tri);
                                    tr.AddNewlyCreatedDBObject(tri, true);
                                    idsForGroup.Add(tri.ObjectId);
                                }
                                else
                                {
                                    Line slopeLine = new Line(sPt, endPoint);
                                    slopeLine.ColorIndex = colorIndex;
                                    SafeSetLayer(slopeLine, layerName);

                                    btr.AppendEntity(slopeLine);
                                    tr.AddNewlyCreatedDBObject(slopeLine, true);
                                    idsForGroup.Add(slopeLine.ObjectId);
                                }

                                // >>> ახალი Curtain (Tick & Circle) ხაზვა <<<
                                if (isShort)
                                {
                                    if (curtainType == "Circle")
                                    {
                                        double rad = step * 0.06;
                                        Point3d cCenter = endPoint + (dir * 2 * (rad * 3));
                                        Circle circ = new Circle(cCenter, Vector3d.ZAxis, rad);
                                        circ.ColorIndex = colorIndex;
                                        SafeSetLayer(circ, layerName);

                                        btr.AppendEntity(circ);
                                        tr.AddNewlyCreatedDBObject(circ, true);
                                        idsForGroup.Add(circ.ObjectId);
                                    }
                                    else if (curtainType == "Tick (T-Shape)")
                                    {
                                        Vector3d perp = dir.CrossProduct(Vector3d.ZAxis).GetNormal();
                                        double tickHalf = step * 0.2;
                                        Point3d t1 = endPoint + (perp * tickHalf);
                                        Point3d t2 = endPoint - (perp * tickHalf);

                                        Line tickLine = new Line(t1, t2);
                                        tickLine.ColorIndex = colorIndex;
                                        SafeSetLayer(tickLine, layerName);

                                        btr.AppendEntity(tickLine);
                                        tr.AddNewlyCreatedDBObject(tickLine, true);
                                        idsForGroup.Add(tickLine.ObjectId);
                                    }
                                }
                            }
                            lineIndex++;
                        }

                        if (createGroup && idsForGroup.Count > 0)
                        {
                            Group grp = new Group("SlopeGroup", true);
                            DBDictionary gd = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
                            gd.SetAt("*", grp);
                            tr.AddNewlyCreatedDBObject(grp, true);
                            grp.Append(idsForGroup);
                        }

                        if (hideBackground)
                        {
                            try
                            {
                                Point3dCollection segmentBoundary = GetSegmentBoundary(crestProj, toeProj, startDist, endDist, step, reverseToe, is3D, crest, toe);

                                if (segmentBoundary.Count > 2)
                                {
                                    using (DbPolyline boundary = new DbPolyline())
                                    {
                                        for (int i = 0; i < segmentBoundary.Count; i++)
                                        {
                                            boundary.AddVertexAt(i, new Point2d(segmentBoundary[i].X, segmentBoundary[i].Y), 0, 0, 0);
                                        }

                                        boundary.Closed = true;
                                        btr.AppendEntity(boundary);
                                        tr.AddNewlyCreatedDBObject(boundary, true);

                                        Hatch hatch = new Hatch();
                                        hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                                        hatch.Color = AcColor.FromColorIndex(ColorMethod.ByAci, (short)backColorIdx);
                                        hatch.Transparency = new Transparency(150);
                                        SafeSetLayer(hatch, layerName);

                                        btr.AppendEntity(hatch);
                                        tr.AddNewlyCreatedDBObject(hatch, true);

                                        hatch.AppendLoop(HatchLoopTypes.Default, new ObjectIdCollection { boundary.ObjectId });
                                        hatch.EvaluateHatch(true);
                                        boundary.Erase();

                                        DrawOrderTable dot = (DrawOrderTable)tr.GetObject(btr.DrawOrderTableId, OpenMode.ForWrite);
                                        dot.MoveToBottom(new ObjectIdCollection { hatch.ObjectId });
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                tr.Commit();
            }
        }

        private bool IsOppositeDirection(Curve c1, Curve c2)
        {
            Point3d s1 = c1.StartPoint;
            Point3d e1 = c1.EndPoint;
            Point3d s2 = c2.StartPoint;
            Point3d e2 = c2.EndPoint;
            Vector3d v1 = e1 - s1;
            Vector3d v2 = e2 - s2;
            return v1.DotProduct(v2) < 0;
        }

        private Point3dCollection GetSegmentBoundary(Curve crestProj, Curve toeProj, double startD, double endD, double step, bool reverseToe, bool is3D, Curve origCrest, Curve origToe)
        {
            Point3dCollection pts = new Point3dCollection();
            double crestLen = crestProj.GetDistanceAtParameter(crestProj.EndParam);
            double toeLen = toeProj.GetDistanceAtParameter(toeProj.EndParam);

            for (double d = startD; d <= endD; d += step)
            {
                double dSafe = Math.Min(d, crestLen - 1e-6);
                Point3d pt = crestProj.GetPointAtDist(dSafe);
                if (is3D)
                {
                    double param = crestProj.GetParameterAtDistance(dSafe);
                    pt = origCrest.GetPointAtParameter(param);
                }
                pts.Add(new Point3d(pt.X, pt.Y, 0));
            }

            for (double d = endD; d >= startD; d -= step)
            {
                double ratio = d / crestLen;
                double toeTargetD = reverseToe ? (1.0 - ratio) * toeLen : ratio * toeLen;
                double dSafe = Math.Max(0, Math.Min(toeTargetD, toeLen - 1e-6));
                Point3d pt = toeProj.GetPointAtDist(dSafe);

                if (is3D)
                {
                    double param = toeProj.GetParameterAtDistance(dSafe);
                    pt = origToe.GetPointAtParameter(param);
                }
                pts.Add(new Point3d(pt.X, pt.Y, 0));
            }
            return pts;
        }

        private Point3d CalculateToePointImproved(double currentDist2D, Curve crest, Curve toe, Curve crestProj, Curve toeProj, double ratio2D, string method, List<AdjustedRange>? adjs, out bool isManual, bool reverseToe = false)
        {
            isManual = false;
            double totalToe = toeProj.GetDistanceAtParameter(toeProj.EndParam);
            double paramToe;

            if (adjs != null && adjs.Count > 0)
            {
                foreach (var adj in adjs)
                {
                    double minC = Math.Min(adj.StartCrest, adj.EndCrest);
                    double maxC = Math.Max(adj.StartCrest, adj.EndCrest);

                    if (currentDist2D >= minC - 0.01 && currentDist2D <= maxC + 0.01)
                    {
                        isManual = true;

                        double crestRange = adj.EndCrest - adj.StartCrest;
                        double t = (Math.Abs(crestRange) < 0.001) ? 0 : (currentDist2D - adj.StartCrest) / crestRange;

                        double finalToe2D = adj.StartToe + t * (adj.EndToe - adj.StartToe);

                        bool toeClosed = toeProj.Closed || toeProj.StartPoint.DistanceTo(toeProj.EndPoint) < 0.01;
                        if (toeClosed)
                        {
                            while (finalToe2D < 0) finalToe2D += totalToe;
                            while (finalToe2D >= totalToe) finalToe2D -= totalToe;
                        }

                        finalToe2D = Math.Max(0, Math.Min(finalToe2D, totalToe - 1e-6));

                        paramToe = toeProj.GetParameterAtDistance(finalToe2D);
                        return toe.GetPointAtParameter(paramToe);
                    }
                }
            }

            if (method == "Perpendicular" || method == "Elastic")
            {
                Point3d pCrestProj = crestProj.GetPointAtDist(currentDist2D);
                Point3d pToeProj = toeProj.GetClosestPointTo(pCrestProj, false);
                paramToe = toeProj.GetParameterAtPoint(pToeProj);
                return toe.GetPointAtParameter(paramToe);
            }

            double effectiveRatio = reverseToe ? (1.0 - ratio2D) : ratio2D;
            double targetDist = effectiveRatio * totalToe;
            targetDist = Math.Max(0, Math.Min(targetDist, totalToe - 1e-6));
            paramToe = toeProj.GetParameterAtDistance(targetDist);
            return toe.GetPointAtParameter(paramToe);
        }

        private void EnsureLayerExists(Database db, string name, int color)
        {
            if (string.IsNullOrEmpty(name)) return;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(name))
                {
                    lt.UpgradeOpen();
                    LayerTableRecord ltr = new LayerTableRecord { Name = name };
                    ltr.Color = AcColor.FromColorIndex(ColorMethod.ByAci, (short)color);
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                tr.Commit();
            }
        }

        private void SafeSetLayer(Entity ent, string layerName)
        {
            if (!string.IsNullOrEmpty(layerName)) { try { ent.Layer = layerName; } catch { ent.Layer = "0"; } }
        }

        private void HighlightEntity(ObjectId id, bool highlight)
        {
            if (id.IsNull) return;
            using (Transaction tr = id.Database.TransactionManager.StartTransaction())
            {
                Entity? ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent != null) { if (highlight) ent.Highlight(); else ent.Unhighlight(); }
                tr.Commit();
            }
        }

        private void DeleteSelectedObjects(Document doc)
        {
            Editor ed = doc.Editor;
            PromptSelectionOptions selOpts = new PromptSelectionOptions();
            selOpts.MessageForAdding = "\nChoose Object(s) To Delete And Click Enter: ";
            PromptSelectionResult selRes = ed.GetSelection(selOpts);
            if (selRes.Status == PromptStatus.OK)
            {
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject selObj in selRes.Value)
                    {
                        if (selObj != null)
                        {
                            Entity? ent = tr.GetObject(selObj.ObjectId, OpenMode.ForWrite) as Entity;
                            ent?.Erase(true);
                        }
                    }
                    tr.Commit();
                }
                ed.WriteMessage("\nObject(s) are Deleted.");
            }
        }

        private void ShowLivePreview(Document doc, ObjectId crestId, ObjectId toeId, double step, double ratio, List<AdjustedRange> adjs, double startD, double endD)
        {
            ClearPreview(doc);

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId, OpenMode.ForWrite);

                Curve crest = (Curve)tr.GetObject(crestId, OpenMode.ForRead);
                Curve toe = (Curve)tr.GetObject(toeId, OpenMode.ForRead);

                using (Curve crestProj = crest.GetProjectedCurve(new Plane(Point3d.Origin, Vector3d.ZAxis), Vector3d.ZAxis) as Curve)
                using (Curve toeProj = toe.GetProjectedCurve(new Plane(Point3d.Origin, Vector3d.ZAxis), Vector3d.ZAxis) as Curve)
                {
                    if (crestProj == null || toeProj == null) return;

                    double lenProj = crestProj.GetDistanceAtParameter(crestProj.EndParam);
                    bool reverseToe = IsOppositeDirection(crestProj, toeProj);

                    if (endD > lenProj) endD = lenProj;

                    for (double d = startD; d <= endD; d += step)
                    {
                        double dSafe = Math.Min(d, lenProj - 1e-6);
                        double ratio2D = (Math.Abs(lenProj) < 0.001) ? 0 : dSafe / lenProj;

                        bool isManual;
                        Point3d pToe = CalculateToePointImproved(dSafe, crest, toe, crestProj, toeProj, ratio2D, "Elastic", adjs, out isManual, reverseToe);

                        Point3d pCrest = crest.GetPointAtParameter(crestProj.GetParameterAtDistance(dSafe));

                        Line l = new Line(pCrest, pToe);
                        l.ColorIndex = isManual ? 3 : 8;

                        btr.AppendEntity(l);
                        tr.AddNewlyCreatedDBObject(l, true);
                        _previewLineIds.Add(l.ObjectId);
                    }
                }
                tr.Commit();
            }
        }

        private void ClearPreview(Document doc)
        {
            if (_previewLineIds.Count == 0) return;

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in _previewLineIds)
                {
                    if (!id.IsNull && id.IsValid && !id.IsErased)
                    {
                        Entity ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                        ent.Erase();
                    }
                }
                tr.Commit();
            }
            _previewLineIds.Clear();
        }
    }

    internal class AreaSelectJig : DrawJig
    {
        private Point3d _startPt;
        private Point3d _currentPt;
        private Curve _crest;
        private Curve _toe;
        private double _step;
        private string _method;
        private bool _is3D;

        private Curve _crestProj;
        private Curve _toeProj;

        public Point3d CurrentMousePoint => _currentPt;

        public AreaSelectJig(Curve crest, Curve toe, Point3d startPt, double step, string method, bool is3D)
        {
            _crest = crest;
            _toe = toe;
            _startPt = startPt;
            _currentPt = startPt;
            _step = step;
            _method = method;
            _is3D = is3D;

            _crestProj = crest.GetProjectedCurve(new Plane(Point3d.Origin, Vector3d.ZAxis), Vector3d.ZAxis) as Curve;
            _toeProj = toe.GetProjectedCurve(new Plane(Point3d.Origin, Vector3d.ZAxis), Vector3d.ZAxis) as Curve;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var res = prompts.AcquirePoint("\nSelect End Point for Area: ");
            if (res.Status != PromptStatus.OK) return SamplerStatus.Cancel;
            if (_currentPt.DistanceTo(res.Value) < 0.001) return SamplerStatus.NoChange;
            _currentPt = res.Value;
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            if (_crestProj == null || _toeProj == null) return false;

            Point3d startPtOnCrest = _crestProj.GetClosestPointTo(_startPt, false);
            Point3d endPtOnCrest = _crestProj.GetClosestPointTo(_currentPt, false);

            double dist1 = _crestProj.GetDistAtPoint(startPtOnCrest);
            double dist2 = _crestProj.GetDistAtPoint(endPtOnCrest);

            double s = Math.Min(dist1, dist2);
            double e = Math.Max(dist1, dist2);

            double crestLenProj = _crestProj.GetDistanceAtParameter(_crestProj.EndParam);
            if (e > crestLenProj) e = crestLenProj;

            for (double d = s; d <= e; d += _step)
            {
                double dSafe = Math.Min(d, crestLenProj - 1e-6);

                Point3d pCrestProj = _crestProj.GetPointAtDist(dSafe);
                Point3d pToeProj = _toeProj.GetClosestPointTo(pCrestProj, false);

                Point3d pCrest3D = _crest.GetPointAtParameter(_crestProj.GetParameterAtDistance(dSafe));
                Point3d pToe3D = _toe.GetPointAtParameter(_toeProj.GetParameterAtPoint(pToeProj));

                draw.Geometry.Draw(new Line(pCrest3D, pToe3D));
            }
            return true;
        }

        ~AreaSelectJig()
        {
            if (_crestProj != null && !_crestProj.IsDisposed) _crestProj.Dispose();
            if (_toeProj != null && !_toeProj.IsDisposed) _toeProj.Dispose();
        }
    }

    internal class SlopeRangeJig : DrawJig
    {
        private readonly Curve _crest;
        private readonly Curve _toe;
        private readonly Curve _crestProj;
        private readonly Curve _toeProj;

        private readonly double _startCrest2D;
        private readonly double _endCrest2D;
        private readonly double _step;

        private readonly bool _crossesSeam;
        private readonly double _totalCrest2D;
        private readonly double _totalToe2D;
        private readonly bool _isToeClosed;

        private Point3d _currentMousePt;
        private double _currentMouseToe2D;
        private double _startToe2D;

        public SlopeRangeJig(Curve crestProj, Curve toeProj, Curve crest, Curve toe, double sCrest2D, double eCrest2D, double step, bool crossesSeam, double totalCrest, double totalToe, bool isToeClosed)
        {
            _crestProj = crestProj;
            _toeProj = toeProj;
            _crest = crest;
            _toe = toe;

            _startCrest2D = sCrest2D;
            _endCrest2D = eCrest2D;
            _step = step;
            _crossesSeam = crossesSeam;
            _totalCrest2D = totalCrest;
            _totalToe2D = totalToe;
            _isToeClosed = isToeClosed;

            _startToe2D = _toeProj.GetDistAtPoint(_toeProj.GetClosestPointTo(_crestProj.GetPointAtDist(_startCrest2D), false));
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var res = prompts.AcquirePoint("\nAdjust range on Toe: ");
            if (res.Status != PromptStatus.OK) return SamplerStatus.Cancel;
            if (res.Value.DistanceTo(_currentMousePt) < 0.001) return SamplerStatus.NoChange;

            Point3d mouseOnToeProj = _toeProj.GetClosestPointTo(res.Value, false);
            double potentialToe2D = _toeProj.GetDistAtPoint(mouseOnToeProj);

            if (!_isToeClosed)
            {
                if (Math.Abs(potentialToe2D - _startToe2D) > _totalToe2D * 0.4)
                {
                    return SamplerStatus.NoChange;
                }
            }

            _currentMousePt = res.Value;
            _currentMouseToe2D = potentialToe2D;

            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            if (draw == null) return false;

            double sToe2D = _startToe2D;
            double toeRange = _currentMouseToe2D - sToe2D;

            if (_isToeClosed)
            {
                if (toeRange > _totalToe2D / 2.0) toeRange -= _totalToe2D;
                else if (toeRange < -_totalToe2D / 2.0) toeRange += _totalToe2D;
            }

            if (_crossesSeam)
            {
                double totalTravel;
                if (_startCrest2D < _endCrest2D)
                {
                    totalTravel = -_startCrest2D - (_totalCrest2D - _endCrest2D);
                    for (double d = _startCrest2D; d >= 0; d -= _step)
                    {
                        DrawInterpolatedLine(d, d - _startCrest2D, totalTravel, sToe2D, toeRange, draw);
                    }
                    for (double d = _totalCrest2D; d >= _endCrest2D; d -= _step)
                    {
                        DrawInterpolatedLine(d, -_startCrest2D - (_totalCrest2D - d), totalTravel, sToe2D, toeRange, draw);
                    }
                }
                else
                {
                    totalTravel = (_totalCrest2D - _startCrest2D) + _endCrest2D;
                    for (double d = _startCrest2D; d <= _totalCrest2D; d += _step)
                    {
                        DrawInterpolatedLine(d, d - _startCrest2D, totalTravel, sToe2D, toeRange, draw);
                    }
                    for (double d = 0; d <= _endCrest2D; d += _step)
                    {
                        DrawInterpolatedLine(d, (_totalCrest2D - _startCrest2D) + d, totalTravel, sToe2D, toeRange, draw);
                    }
                }
            }
            else
            {
                double totalTravel = _endCrest2D - _startCrest2D;
                double minD = Math.Min(_startCrest2D, _endCrest2D);
                double maxD = Math.Max(_startCrest2D, _endCrest2D);

                for (double d = minD; d <= maxD; d += _step)
                {
                    DrawInterpolatedLine(d, d - _startCrest2D, totalTravel, sToe2D, toeRange, draw);
                }
            }
            return true;
        }

        private void DrawInterpolatedLine(double crestD, double traversedD, double totalL, double sToeD, double toeRng, WorldDraw draw)
        {
            double rel = (Math.Abs(totalL) < 0.001) ? 0 : traversedD / totalL;
            double targetToe2D = sToeD + rel * toeRng;

            if (_isToeClosed)
            {
                while (targetToe2D > _totalToe2D) targetToe2D -= _totalToe2D;
                while (targetToe2D < 0) targetToe2D += _totalToe2D;
            }

            crestD = Math.Max(0, Math.Min(crestD, _totalCrest2D - 1e-6));
            Point3d pCrest = _crest.GetPointAtParameter(_crestProj.GetParameterAtDistance(crestD));

            targetToe2D = Math.Max(0, Math.Min(targetToe2D, _totalToe2D - 1e-6));
            Point3d pToe = _toe.GetPointAtParameter(_toeProj.GetParameterAtDistance(targetToe2D));

            using (Line l = new Line(pCrest, pToe))
            {
                l.ColorIndex = 4;
                draw.Geometry.Draw(l);
            }
        }

        public Point3d CurrentMousePt3D => _currentMousePt;
    }
}