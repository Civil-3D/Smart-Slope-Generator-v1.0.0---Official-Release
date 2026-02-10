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

// Alias to prevent ambiguity
using DbPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace Draw_Slopes
{
    public struct AdjustedRange
    {
        public double StartCrestDist;
        public double EndCrestDist;
        public double TargetToeDist;
    }

    public class SlopeLogic
    {
        private static List<Line> _previewLines = new List<Line>();

        [CommandMethod("DRAWSLOPE")]
        public void RunSlopeCommand()
        {
            Document doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            ClearPreview();

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

                // --- Read Data ---
                double step = window.Step;
                double offset = window.Offset;
                double ratio = window.Ratio > 1 ? window.Ratio / 100.0 : window.Ratio;
                int colorIdx = window.SelectedColorIndex;
                string layerName = window.LayerName;
                bool isGroup = window.IsGroup;
                bool isBackground = window.IsBackground;
                int backColorIdx = window.BackgroundHatchColorIndex;
                bool isFixed = window.IsFixedLength;
                string method = window.Method;
                bool autoOrient = window.AutoOrient;
                bool isCurtain = window.IsCurtain;
                bool is3D = window.Is3D;

                double globalStartDist = 0;
                double globalEndDist = 0;
                List<AdjustedRange> manualAdjustments = new List<AdjustedRange>();

                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    Curve? crest = tr.GetObject(res1.ObjectId, OpenMode.ForRead) as Curve;
                    Curve? toe = tr.GetObject(res2.ObjectId, OpenMode.ForRead) as Curve;

                    if (crest != null && toe != null)
                    {
                        // 1. Ask User: Entire or Segment
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

                        double totalLen = crest.GetDistanceAtParameter(crest.EndParam);
                        globalEndDist = totalLen;

                        // 2. Custom (Segment) Logic
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

                            Point3d startPt = crest.GetClosestPointTo(pprStart.Value, false);
                            globalStartDist = crest.GetDistAtPoint(startPt);

                            // JIG Area Selection
                            try
                            {
                                var selectionJig = new AreaSelectJig(crest, toe, startPt, step, method, is3D);
                                PromptResult jigRes = ed.Drag(selectionJig);

                                if (jigRes.Status == PromptStatus.OK)
                                {
                                    Point3d endPt = crest.GetClosestPointTo(selectionJig.CurrentMousePoint, false);
                                    globalEndDist = crest.GetDistAtPoint(endPt);

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
                                    continue;
                                }
                            }
                            catch { }
                        }

                        // 3. Elastic Adjustment (The Magic Logic)
                        if (method == "Elastic")
                        {
                            while (true)
                            {
                                ShowLivePreview(crest, toe, step, ratio, manualAdjustments, globalStartDist, globalEndDist);
                                ed.Regen();

                                var ppo1 = new PromptPointOptions("\nSelect Section start for adjustment or press");
                                ppo1.Keywords.Add("ENTER");
                                ppo1.AppendKeywordsToMessage = true;
                                ppo1.AllowNone = true;

                                var ppr1 = ed.GetPoint(ppo1);
                                if (ppr1.Status != PromptStatus.OK) break;

                                Point3d startPt3D = crest.GetClosestPointTo(ppr1.Value, false);
                                double clickedDist = crest.GetDistAtPoint(startPt3D);

                                if (clickedDist < globalStartDist - 0.001 || clickedDist > globalEndDist + 0.001)
                                {
                                    ed.WriteMessage("\nPlease select a point within the defined drawing range.");
                                    continue;
                                }

                                var ppo2 = new PromptPointOptions("\nSelect section end: ");
                                ppo2.UseBasePoint = true;
                                ppo2.BasePoint = ppr1.Value;
                                var ppr2 = ed.GetPoint(ppo2);
                                if (ppr2.Status != PromptStatus.OK) break;

                                Point3d endPt3D = crest.GetClosestPointTo(ppr2.Value, false);
                                double clickedEndDist = crest.GetDistAtPoint(endPt3D);

                                if (clickedEndDist < globalStartDist) clickedEndDist = globalStartDist;
                                if (clickedEndDist > globalEndDist) clickedEndDist = globalEndDist;
                                endPt3D = crest.GetPointAtDist(clickedEndDist);

                                // JIG for Manual Adjustment
                                var jig = new SlopeRangeJig(crest, toe, startPt3D, endPt3D, step, ratio);

                                if (ed.Drag(jig).Status == PromptStatus.OK)
                                {
                                    double sDist = crest.GetDistAtPoint(startPt3D);
                                    double eDist = crest.GetDistAtPoint(endPt3D);
                                    Point3d finalMousePt = jig.CurrentMousePt3D;
                                    double targetToeDist = toe.GetDistAtPoint(toe.GetClosestPointTo(finalMousePt, false));

                                    var newAdj = new AdjustedRange
                                    {
                                        StartCrestDist = sDist,
                                        EndCrestDist = eDist,
                                        TargetToeDist = targetToeDist
                                    };

                                    AddAdjustmentWithSplitting(manualAdjustments, newAdj, crest, toe);
                                }
                                jig.DisposeCurves();
                            }
                        }
                        ClearPreview();
                    }
                    tr.Commit();
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
                    isFixed,
                    method,
                    autoOrient,
                    isCurtain,
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

        private void AddAdjustmentWithSplitting(List<AdjustedRange> list, AdjustedRange newAdj, Curve crest, Curve toe)
        {
            var newList = new List<AdjustedRange>();
            foreach (var old in list)
            {
                double oldMin = Math.Min(old.StartCrestDist, old.EndCrestDist);
                double oldMax = Math.Max(old.StartCrestDist, old.EndCrestDist);
                double newMin = Math.Min(newAdj.StartCrestDist, newAdj.EndCrestDist);
                double newMax = Math.Max(newAdj.StartCrestDist, newAdj.EndCrestDist);

                if (oldMax <= newMin + 0.001 || oldMin >= newMax - 0.001)
                {
                    newList.Add(old);
                    continue;
                }

                // Logic for splitting overlapping adjustments (RESTORED)
                if (oldMin < newMin)
                {
                    double splitCrest = newMin;
                    double oldToeStart = toe.GetDistAtPoint(toe.GetClosestPointTo(crest.GetPointAtDist(old.StartCrestDist), false));
                    double range = old.EndCrestDist - old.StartCrestDist;
                    double rel = (Math.Abs(range) < 0.001) ? 0 : (splitCrest - old.StartCrestDist) / range;
                    double splitToeTarget = oldToeStart + (rel * (old.TargetToeDist - oldToeStart));

                    newList.Add(new AdjustedRange { StartCrestDist = old.StartCrestDist, EndCrestDist = splitCrest, TargetToeDist = splitToeTarget });
                }
                if (oldMax > newMax)
                {
                    newList.Add(new AdjustedRange { StartCrestDist = newMax, EndCrestDist = old.EndCrestDist, TargetToeDist = old.TargetToeDist });
                }
            }
            newList.Add(newAdj);
            list.Clear();
            list.AddRange(newList);
        }

        public void DrawSlopeHatch(ObjectId crestId, ObjectId toeId, double step, double ratio, int colorIndex, bool createGroup, string layerName, bool hideBackground, int backColorIdx, double offset, bool isFixed, string selectedMethod, bool autoOrient, bool useBlanket, bool is3D, List<AdjustedRange>? manualAdjs, double startDist, double endDist)
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
                        bool isLong = true;

                        // Check Direction
                        bool reverseToe = IsOppositeDirection(crestProj, toeProj);

                        if (endDist > crestLenProj) endDist = crestLenProj;
                        if (startDist < 0) startDist = 0;

                        for (double d = startDist; d <= endDist; d += step)
                        {
                            double param = crestProj.GetParameterAtDistance(Math.Min(d, crestLenProj - 1e-6));
                            Point3d pCrest3D = crest.GetPointAtParameter(param);
                            double currentDist3D = crest.GetDistAtPoint(pCrest3D);

                            double ratio2D = (Math.Abs(crestLenProj) < 0.001) ? 0 : d / crestLenProj;

                            bool dummy;
                            // RESTORED: Passing all parameters correctly
                            Point3d pToe = CalculateToePointImproved(pCrest3D, currentDist3D, crest, toe, crestProj, toeProj, ratio2D, selectedMethod, manualAdjs, out dummy, reverseToe);

                            Point3d sPt = is3D ? pCrest3D : new Point3d(pCrest3D.X, pCrest3D.Y, 0);
                            Point3d ePt = is3D ? pToe : new Point3d(pToe.X, pToe.Y, 0);

                            if (autoOrient && sPt.Z < ePt.Z) { var temp = sPt; sPt = ePt; ePt = temp; }

                            Vector3d fullVec = ePt - sPt;
                            if (fullVec.Length < 0.001) continue;

                            double drawLen = (!isFixed && !isLong) ? Math.Max(0, fullVec.Length - offset) * ratio : fullVec.Length - offset;

                            if (drawLen > 0.01)
                            {
                                Vector3d dir = fullVec.GetNormal();
                                Point3d endPoint = sPt + (dir * drawLen);

                                Line slopeLine = new Line(sPt, endPoint);
                                slopeLine.ColorIndex = colorIndex;
                                SafeSetLayer(slopeLine, layerName);

                                btr.AppendEntity(slopeLine);
                                tr.AddNewlyCreatedDBObject(slopeLine, true);
                                idsForGroup.Add(slopeLine.ObjectId);

                                if (useBlanket && !isLong)
                                {
                                    double rad = step * 0.06;
                                    Point3d cCenter = endPoint + (dir * 2 *(rad * 3));
                                    Circle circ = new Circle(cCenter, Vector3d.ZAxis, rad);
                                    circ.ColorIndex = colorIndex;
                                    SafeSetLayer(circ, layerName);

                                    btr.AppendEntity(circ);
                                    tr.AddNewlyCreatedDBObject(circ, true);
                                    idsForGroup.Add(circ.ObjectId);
                                }
                            }
                            isLong = !isLong;
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

        private Point3d CalculateToePointImproved(Point3d pCrest3D, double currentDist3D, Curve crest, Curve toe, Curve crestProj, Curve toeProj, double ratio2D, string method, List<AdjustedRange>? adjs, out bool isManual, bool reverseToe = false)
        {
            isManual = false;

            double effectiveRatio = reverseToe ? (1.0 - ratio2D) : ratio2D;
            double paramToe; // <--- ვქმნით ერთხელ აქ, რომ ყველგან გამოვიყენოთ

            // 1. Manual Adjustment Check
            if (adjs != null && adjs.Count > 0)
            {
                foreach (var adj in adjs)
                {
                    if (currentDist3D >= Math.Min(adj.StartCrestDist, adj.EndCrestDist) - 0.01 &&
                        currentDist3D <= Math.Max(adj.StartCrestDist, adj.EndCrestDist) + 0.01)
                    {
                        isManual = true;

                        Point3d sCrestPt3D = crest.GetPointAtDist(adj.StartCrestDist);
                        Point3d eCrestPt3D = crest.GetPointAtDist(adj.EndCrestDist);

                        Point3d sCrestPt2D = crestProj.GetClosestPointTo(sCrestPt3D, false);
                        Point3d eCrestPt2D = crestProj.GetClosestPointTo(eCrestPt3D, false);

                        double sCrestDist2D = crestProj.GetDistAtPoint(sCrestPt2D);
                        double eCrestDist2D = crestProj.GetDistAtPoint(eCrestPt2D);

                        Point3d currentPt2D = crestProj.GetClosestPointTo(pCrest3D, false);
                        double currentDist2D = crestProj.GetDistAtPoint(currentPt2D);

                        double range2D = eCrestDist2D - sCrestDist2D;
                        double rel = (Math.Abs(range2D) < 0.001) ? 0 : (currentDist2D - sCrestDist2D) / range2D;

                        Point3d sToePt3D = toe.GetClosestPointTo(sCrestPt3D, false);
                        Point3d targetToePt3D = toe.GetPointAtDist(adj.TargetToeDist);

                        Point3d sToePt2D = toeProj.GetClosestPointTo(sToePt3D, false);
                        Point3d targetToePt2D = toeProj.GetClosestPointTo(targetToePt3D, false);

                        double sToeDist2D = toeProj.GetDistAtPoint(sToePt2D);
                        double targetToeDist2D = toeProj.GetDistAtPoint(targetToePt2D);

                        double finalToeDist2D = sToeDist2D + (rel * (targetToeDist2D - sToeDist2D));

                        paramToe = toeProj.GetParameterAtDistance(finalToeDist2D); // აქ აღარ ვწერთ 'double'-ს
                        return toe.GetPointAtParameter(paramToe);
                    }
                }
            }

            // 2. Standard Methods (Perpendicular / Elastic)
            if (method == "Perpendicular" || method == "Elastic")
            {
                Point3d pCrestProj = crestProj.GetClosestPointTo(pCrest3D, false);
                Point3d pToeProj = toeProj.GetClosestPointTo(pCrestProj, false);
                paramToe = toeProj.GetParameterAtPoint(pToeProj); // აქაც ვიყენებთ უკვე შექმნილს
                return toe.GetPointAtParameter(paramToe);
            }

            // 3. Fallback (Hybrid / Relative)
            double toeLen = toeProj.GetDistanceAtParameter(toeProj.EndParam);
            double targetDist = effectiveRatio * toeLen;
            targetDist = Math.Max(0, Math.Min(targetDist, toeLen - 1e-6));

            paramToe = toeProj.GetParameterAtDistance(targetDist); // აქაც
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

        private void ShowLivePreview(Curve crest, Curve toe, double step, double ratio, List<AdjustedRange> adjs, double startD, double endD)
        {
            ClearPreview();

            using (Curve crestProj = crest.GetProjectedCurve(new Plane(Point3d.Origin, Vector3d.ZAxis), Vector3d.ZAxis) as Curve)
            using (Curve toeProj = toe.GetProjectedCurve(new Plane(Point3d.Origin, Vector3d.ZAxis), Vector3d.ZAxis) as Curve)
            {
                if (crestProj == null || toeProj == null) return;

                double lenProj = crestProj.GetDistanceAtParameter(crestProj.EndParam);

                bool reverseToe = IsOppositeDirection(crestProj, toeProj);

                if (endD > lenProj) endD = lenProj;

                for (double d = startD; d <= endD; d += step)
                {
                    double param = crestProj.GetParameterAtDistance(Math.Min(d, lenProj - 1e-6));
                    Point3d pCrest = crest.GetPointAtParameter(param);
                    double currentDist3D = crest.GetDistAtPoint(pCrest);
                    double ratio2D = (Math.Abs(lenProj) < 0.001) ? 0 : d / lenProj;

                    bool isManual;
                    // Passing reverseToe here ensures Preview looks correct for automatic parts, 
                    // and 'adjs' logic inside handles manual parts.
                    Point3d pToe = CalculateToePointImproved(pCrest, currentDist3D, crest, toe, crestProj, toeProj, ratio2D, "Elastic", adjs, out isManual, reverseToe);

                    Line l = new Line(pCrest, pToe);
                    l.ColorIndex = isManual ? 3 : 8;
                    TransientManager.CurrentTransientManager.AddTransient(l, TransientDrawingMode.Main, 128, new IntegerCollection());
                    _previewLines.Add(l);
                }
            }
        }

        private void ClearPreview()
        {
            foreach (var l in _previewLines)
            {
                if (l != null && !l.IsDisposed)
                {
                    TransientManager.CurrentTransientManager.EraseTransient(l, new IntegerCollection());
                    l.Dispose();
                }
            }
            _previewLines.Clear();
        }
    }

    // --- JIG CLASSES ---
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

            Point3d startPtOnCrest = _crest.GetClosestPointTo(_startPt, false);
            Point3d endPtOnCrest = _crest.GetClosestPointTo(_currentPt, false);

            double dist1 = _crest.GetDistAtPoint(startPtOnCrest);
            double dist2 = _crest.GetDistAtPoint(endPtOnCrest);

            double s = Math.Min(dist1, dist2);
            double e = Math.Max(dist1, dist2);

            double crestLenProj = _crestProj.GetDistanceAtParameter(_crestProj.EndParam);
            if (e > crestLenProj) e = crestLenProj;

            for (double d = s; d <= e; d += _step)
            {
                double dSafe = Math.Min(d, crestLenProj - 1e-6);
                double param = _crestProj.GetParameterAtDistance(dSafe);
                Point3d pCrest3D = _crest.GetPointAtParameter(param);

                Point3d pCrestProj = _crestProj.GetClosestPointTo(pCrest3D, false);
                Point3d pToeProj = _toeProj.GetClosestPointTo(pCrestProj, false);
                double paramToe = _toeProj.GetParameterAtPoint(pToeProj);
                Point3d pToe3D = _toe.GetPointAtParameter(paramToe);

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

        private readonly double _startDist2D;
        private readonly double _endDist2D;
        private readonly double _step;
        private readonly double _ratio;

        public double CurrentMouseToeDist2D;
        private Point3d _currentMousePt;

        public SlopeRangeJig(Curve crest, Curve toe, Point3d startPt3D, Point3d endPt3D, double step, double ratio)
        {
            _crest = crest ?? throw new ArgumentNullException(nameof(crest));
            _toe = toe ?? throw new ArgumentNullException(nameof(toe));

            _crestProj = crest.GetProjectedCurve(new Plane(Point3d.Origin, Vector3d.ZAxis), Vector3d.ZAxis) as Curve;
            _toeProj = toe.GetProjectedCurve(new Plane(Point3d.Origin, Vector3d.ZAxis), Vector3d.ZAxis) as Curve;

            Point3d startPt2D = _crestProj.GetClosestPointTo(startPt3D, false);
            Point3d endPt2D = _crestProj.GetClosestPointTo(endPt3D, false);

            _startDist2D = _crestProj.GetDistAtPoint(startPt2D);
            _endDist2D = _crestProj.GetDistAtPoint(endPt2D);

            _step = step;
            _ratio = ratio;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var res = prompts.AcquirePoint("\nAdjust range on Toe: ");
            if (res.Status != PromptStatus.OK) return SamplerStatus.Cancel;
            if (res.Value.DistanceTo(_currentMousePt) < 0.001) return SamplerStatus.NoChange;

            _currentMousePt = res.Value;

            Point3d mouseOnToe2D = _toeProj.GetClosestPointTo(_currentMousePt, false);
            CurrentMouseToeDist2D = _toeProj.GetDistAtPoint(mouseOnToe2D);

            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw)
        {
            if (draw == null) return false;

            Point3d sCrestPt2D = _crestProj.GetPointAtDist(_startDist2D);
            Point3d sToePt2D = _toeProj.GetClosestPointTo(sCrestPt2D, false);
            double sToeDist2D = _toeProj.GetDistAtPoint(sToePt2D);

            double minD = Math.Min(_startDist2D, _endDist2D);
            double maxD = Math.Max(_startDist2D, _endDist2D);

            for (double d = minD; d <= maxD; d += _step)
            {
                double paramCrest = _crestProj.GetParameterAtDistance(d);
                Point3d pCrest3D = _crest.GetPointAtParameter(paramCrest);

                double range = _endDist2D - _startDist2D;
                double rel = (Math.Abs(range) < 0.001) ? 0 : (d - _startDist2D) / range;

                double targetToeDist2D = sToeDist2D + (rel * (CurrentMouseToeDist2D - sToeDist2D));
                targetToeDist2D = Math.Max(0, Math.Min(targetToeDist2D, _toeProj.GetDistanceAtParameter(_toeProj.EndParam)));

                double paramToe = _toeProj.GetParameterAtDistance(targetToeDist2D);
                Point3d pToe3D = _toe.GetPointAtParameter(paramToe);

                using (Line l = new Line(pCrest3D, pToe3D))
                {
                    l.ColorIndex = 4;
                    draw.Geometry.Draw(l);
                }
            }
            return true;
        }

        public void DisposeCurves()
        {
            if (_crestProj != null && !_crestProj.IsDisposed) _crestProj.Dispose();
            if (_toeProj != null && !_toeProj.IsDisposed) _toeProj.Dispose();
        }
        public Point3d CurrentMousePt3D => _currentMousePt;
    }
}