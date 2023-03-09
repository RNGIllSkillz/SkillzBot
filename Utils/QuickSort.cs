using SkillzBot.MODELS;
using System.Collections.Generic;

namespace SkillzBot.Utils
{
    internal sealed class QuickSort
    {
        public List<TrackedMessages> SortArray(List<TrackedMessages> array, int leftIndex, int rightIndex)
        {
            var i = leftIndex;
            var j = rightIndex;
            var pivot = array[leftIndex].TimeStamp;
            while (i <= j)
            {
                while (array[i].TimeStamp < pivot)
                {
                    i++;
                }

                while (array[j].TimeStamp > pivot)
                {
                    j--;
                }
                if (i <= j)
                {
                    (array[j], array[i]) = (array[i], array[j]);
                    i++;
                    j--;
                }
            }
            if (leftIndex < j)
                SortArray(array, leftIndex, j);
            if (i < rightIndex)
                SortArray(array, i, rightIndex);
            return array;
        }
    }
}
