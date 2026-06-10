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
            IDictionary<int, GuideObject> targets,
            Dictionary<int, Vector3> initialPositions)
        {
            var positionInfos = targets.Select(kvp => new GuideCommand.EqualsInfo
            {
                dicKey = kvp.Key,
                oldValue = initialPositions[kvp.Key],
                newValue = kvp.Value.changeAmount.pos,
            }).ToArray();

            Singleton<UndoRedoManager>.Instance.Push(new MultiEqualsCommand(positionInfos, null, null));
        }

        public void PushRotateUndo(
            IDictionary<int, GuideObject> targets,
            Dictionary<int, Vector3> initialPositions,
            Dictionary<int, Vector3> initialRotations)
        {
            var rotationInfos = targets.Select(kvp => new GuideCommand.EqualsInfo
            {
                dicKey = kvp.Key,
                oldValue = initialRotations[kvp.Key],
                newValue = kvp.Value.changeAmount.rot,
            }).ToArray();

            bool anyMoved = targets.Any(kvp =>
                kvp.Value.enablePos && initialPositions[kvp.Key] != kvp.Value.changeAmount.pos);

            GuideCommand.EqualsInfo[] positionInfos = null;
            if (anyMoved)
            {
                positionInfos = targets.Where(kvp => kvp.Value.enablePos).Select(kvp => new GuideCommand.EqualsInfo
                {
                    dicKey = kvp.Key,
                    oldValue = initialPositions[kvp.Key],
                    newValue = kvp.Value.changeAmount.pos,
                }).ToArray();
            }
            
            Singleton<UndoRedoManager>.Instance.Push(new MultiEqualsCommand(positionInfos, null, rotationInfos));
        }

        public void PushScaleUndo(
            IDictionary<int, GuideObject> targets,
            Dictionary<int, Vector3> initialPositions,
            Dictionary<int, Vector3> initialScales)
        {
            var positionInfos = targets.Where(kvp => kvp.Value.enablePos).Select(kvp => new GuideCommand.EqualsInfo
            {
                dicKey = kvp.Key,
                oldValue = initialPositions[kvp.Key],
                newValue = kvp.Value.changeAmount.pos,
            }).ToArray();

            var rotationInfos = targets.Select(kvp => new GuideCommand.EqualsInfo
            {
                dicKey = kvp.Key,
                oldValue = initialScales[kvp.Key],
                newValue = kvp.Value.changeAmount.scale,
            }).ToArray();


            Singleton<UndoRedoManager>.Instance.Push(new MultiEqualsCommand(positionInfos, rotationInfos, null));
        }
    }
}