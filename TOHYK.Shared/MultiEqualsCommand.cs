using Studio;

namespace TOHYK
{
    public class MultiEqualsCommand : ICommand
    {
        private GuideCommand.EqualsInfo[] moveInfo;
        private GuideCommand.EqualsInfo[] scaleInfo;
        private GuideCommand.EqualsInfo[] rotationInfo;

        public MultiEqualsCommand(GuideCommand.EqualsInfo[] moveInfo, GuideCommand.EqualsInfo[] scaleInfo,
            GuideCommand.EqualsInfo[] rotationInfo)
        {
            this.moveInfo = moveInfo;
            this.scaleInfo = scaleInfo;
            this.rotationInfo = rotationInfo;
        }

        public void Do()
        {
            if (moveInfo != null && moveInfo.Length > 0)
                for (int index = 0; index < moveInfo.Length; ++index)
                {
                    ChangeAmount changeAmount = Studio.Studio.GetChangeAmount(moveInfo[index].dicKey);
                    if (changeAmount != null)
                        changeAmount.pos = moveInfo[index].newValue;
                }

            if (scaleInfo != null && scaleInfo.Length > 0)
                for (int index = 0; index < scaleInfo.Length; ++index)
                {
                    ChangeAmount changeAmount = Studio.Studio.GetChangeAmount(scaleInfo[index].dicKey);
                    if (changeAmount != null)
                        changeAmount.scale = scaleInfo[index].newValue;
                }

            if (rotationInfo != null && rotationInfo.Length > 0)
                for (int index = 0; index < rotationInfo.Length; ++index)
                {
                    ChangeAmount changeAmount = Studio.Studio.GetChangeAmount(rotationInfo[index].dicKey);
                    if (changeAmount != null)
                        changeAmount.rot = rotationInfo[index].newValue;
                }
        }

        public void Redo() => Do();

        public void Undo()
        {
            if (moveInfo != null && moveInfo.Length > 0)
                for (int index = 0; index < moveInfo.Length; ++index)
                {
                    ChangeAmount changeAmount = Studio.Studio.GetChangeAmount(moveInfo[index].dicKey);
                    if (changeAmount != null)
                        changeAmount.pos = moveInfo[index].oldValue;
                }

            if (scaleInfo != null && scaleInfo.Length > 0)
                for (int index = 0; index < scaleInfo.Length; ++index)
                {
                    ChangeAmount changeAmount = Studio.Studio.GetChangeAmount(scaleInfo[index].dicKey);
                    if (changeAmount != null)
                        changeAmount.scale = scaleInfo[index].oldValue;
                }

            if (rotationInfo != null && rotationInfo.Length > 0)
                for (int index = 0; index < rotationInfo.Length; ++index)
                {
                    ChangeAmount changeAmount = Studio.Studio.GetChangeAmount(rotationInfo[index].dicKey);
                    if (changeAmount != null)
                        changeAmount.rot = rotationInfo[index].oldValue;
                }
        }
    }
}