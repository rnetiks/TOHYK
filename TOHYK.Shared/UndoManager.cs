using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Studio;
using UnityEngine;
using GuideCommand = Studio.GuideCommand;

namespace TOHYK
{
    public class UndoManager
    {
        public void PushMoveUndo(
            IDictionary<int, ITransformTarget> targets,
            Dictionary<int, Vector3> initialPositions)
        {
            var positionInfos = targets.Select(kvp => new GuideCommand.EqualsInfo
            {
                dicKey = kvp.Key,
                oldValue = initialPositions[kvp.Key],
                newValue = kvp.Value.PosLocal,
            }).ToArray();

            Singleton<UndoRedoManager>.Instance.Push(new MultiEqualsCommand(positionInfos, null, null));
        }

        public void PushRotateUndo(
            IDictionary<int, ITransformTarget> targets,
            Dictionary<int, Vector3> initialPositions,
            Dictionary<int, Vector3> initialRotations)
        {
            var rotationInfos = targets.Select(kvp => new GuideCommand.EqualsInfo
            {
                dicKey = kvp.Key,
                oldValue = initialRotations[kvp.Key],
                newValue = kvp.Value.RotLocal,
            }).ToArray();

            bool anyMoved = targets.Any(kvp =>
                kvp.Value.enablePos && initialPositions[kvp.Key] != kvp.Value.PosLocal);

            GuideCommand.EqualsInfo[] positionInfos = null;
            if (anyMoved)
            {
                positionInfos = targets.Where(kvp => kvp.Value.enablePos).Select(kvp => new GuideCommand.EqualsInfo
                {
                    dicKey = kvp.Key,
                    oldValue = initialPositions[kvp.Key],
                    newValue = kvp.Value.PosLocal,
                }).ToArray();
            }
            
            Singleton<UndoRedoManager>.Instance.Push(new MultiEqualsCommand(positionInfos, null, rotationInfos));
        }

        public void PushScaleUndo(
            IDictionary<int, ITransformTarget> targets,
            Dictionary<int, Vector3> initialPositions,
            Dictionary<int, Vector3> initialScales)
        {
            var positionInfos = targets.Where(kvp => kvp.Value.enablePos).Select(kvp => new GuideCommand.EqualsInfo
            {
                dicKey = kvp.Key,
                oldValue = initialPositions[kvp.Key],
                newValue = kvp.Value.PosLocal,
            }).ToArray();

            var rotationInfos = targets.Select(kvp => new GuideCommand.EqualsInfo
            {
                dicKey = kvp.Key,
                oldValue = initialScales[kvp.Key],
                newValue = kvp.Value.ScaleLocal,
            }).ToArray();


            Singleton<UndoRedoManager>.Instance.Push(new MultiEqualsCommand(positionInfos, rotationInfos, null));
        }

        public void PushMirrorUndo(
            IDictionary<int, ITransformTarget> targets,
            Dictionary<int, Vector3> initialPositions,
            Dictionary<int, Vector3> initialRotations,
            Dictionary<int, Vector3> initialScales)
        {
            var positionInfos = targets.Where(kvp => kvp.Value.enablePos).Select(kvp => new GuideCommand.EqualsInfo
            {
                dicKey = kvp.Key,
                oldValue = initialPositions[kvp.Key],
                newValue = kvp.Value.PosLocal,
            }).ToArray();

            var rotationInfos = targets.Where(kvp => kvp.Value.enableRot).Select(kvp => new GuideCommand.EqualsInfo
            {
                dicKey = kvp.Key,
                oldValue = initialRotations[kvp.Key],
                newValue = kvp.Value.RotLocal,
            }).ToArray();

            var scaleInfos = targets.Where(kvp => kvp.Value.enableScale).Select(kvp => new GuideCommand.EqualsInfo
            {
                dicKey = kvp.Key,
                oldValue = initialScales[kvp.Key],
                newValue = kvp.Value.ScaleLocal,
            }).ToArray();

            Singleton<UndoRedoManager>.Instance.Push(new MultiEqualsCommand(positionInfos, scaleInfos, rotationInfos));
        }


        private const int AccessoryUndoLimit = 50;

        private readonly List<AccessoryUndoBatch> _accessoryUndoStack = new List<AccessoryUndoBatch>();
        private readonly List<AccessoryUndoBatch> _accessoryRedoStack = new List<AccessoryUndoBatch>();

        private struct AccessoryUndoEntry
        {
            public ITransformTarget Target;
            public Vector3 OldPos, OldRot, OldScale;
            public Vector3 NewPos, NewRot, NewScale;

            public string OldParentKey, NewParentKey;
        }

        private class AccessoryUndoBatch
        {
            public List<AccessoryUndoEntry> Entries;
        }

        public bool CanUndoAccessory => _accessoryUndoStack.Count > 0;
        public bool CanRedoAccessory => _accessoryRedoStack.Count > 0;

        public struct AccessoryChange
        {
            public ITransformTarget Target;
            public Vector3 OldPos, OldRot, OldScale;

            public string OldParentKey, NewParentKey;

            public AccessoryChange(ITransformTarget target, Vector3 oldPos, Vector3 oldRot, Vector3 oldScale,
                string oldParentKey = null, string newParentKey = null)
            {
                Target = target;
                OldPos = oldPos;
                OldRot = oldRot;
                OldScale = oldScale;
                OldParentKey = oldParentKey;
                NewParentKey = newParentKey;
            }
        }

        public void PushAccessoryUndo(IEnumerable<AccessoryChange> changes)
        {
            var entries = new List<AccessoryUndoEntry>();
            foreach (var change in changes)
            {
                entries.Add(new AccessoryUndoEntry
                {
                    Target = change.Target,
                    OldPos = change.OldPos,
                    OldRot = change.OldRot,
                    OldScale = change.OldScale,
                    NewPos = change.Target.PosLocal,
                    NewRot = change.Target.RotLocal,
                    NewScale = change.Target.ScaleLocal,
                    OldParentKey = change.OldParentKey,
                    NewParentKey = change.NewParentKey,
                });
            }

            if (entries.Count == 0)
                return;

            _accessoryUndoStack.Add(new AccessoryUndoBatch { Entries = entries });

            if (_accessoryUndoStack.Count > AccessoryUndoLimit)
                _accessoryUndoStack.RemoveAt(0);

            _accessoryRedoStack.Clear();
        }

        public void ClearAccessoryUndo()
        {
            _accessoryUndoStack.Clear();
            _accessoryRedoStack.Clear();
        }

        public void UndoAccessory() => Step(_accessoryUndoStack, _accessoryRedoStack, useOldValue: true);

        public void RedoAccessory() => Step(_accessoryRedoStack, _accessoryUndoStack, useOldValue: false);

        private static void Step(List<AccessoryUndoBatch> from, List<AccessoryUndoBatch> to, bool useOldValue)
        {
            while (from.Count > 0)
            {
                var batch = from[from.Count - 1];
                from.RemoveAt(from.Count - 1);

                bool anySucceeded = false;
                foreach (var entry in batch.Entries)
                {
                    try
                    {
#if KKS
                        string parentKeyToApply = useOldValue ? entry.OldParentKey : entry.NewParentKey;
                        if (parentKeyToApply != null && entry.Target is AccessoryTransformTarget accessoryTarget)
                            accessoryTarget.SetParentKey(parentKeyToApply);
#endif

                        entry.Target.PosLocal = useOldValue ? entry.OldPos : entry.NewPos;
                        entry.Target.RotLocal = useOldValue ? entry.OldRot : entry.NewRot;
                        entry.Target.ScaleLocal = useOldValue ? entry.OldScale : entry.NewScale;
                        entry.Target.Commit();
                        anySucceeded = true;
                    }
                    catch
                    {
                    }
                }

                if (anySucceeded)
                {
                    to.Add(batch);
                    return;
                }
            }
        }
    }
}
