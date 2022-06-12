using UnityEngine;
using System.Collections.Generic;

namespace NaughtyBezierCurves
{
    public class BezierCurve3D : MonoBehaviour
    {
        // Serializable Fields
        [SerializeField]
        [Tooltip("The color used to render the curve")]
        private Color curveColor = Color.green;

        [SerializeField]
        [Tooltip("The color used to render the start point of the curve")]
        private Color startPointColor = Color.red;

        [SerializeField]
        [Tooltip("The color used to render the end point of the curve")]
        private Color endPointColor = Color.blue;

        [SerializeField]
        [Tooltip("The number of segments that the curve has. Affects calculations and performance")]
        private int sampling = 25;

        [SerializeField]
        [HideInInspector]
        private List<BezierPoint3D> keyPoints = new List<BezierPoint3D>();

        [SerializeField]
        [Range(0f, 1f)]
        float normalizedTime = 0.5f;

        public struct LUTPoint { public int index; public Vector3 position; public float time; }
        private Dictionary<int, LUTPoint[]> m_cachedLUTs = new Dictionary<int, LUTPoint[]>();

        // Properties        
        public int Sampling
        {
            get
            {
                return this.sampling;
            }
            set
            {
                this.sampling = value;
            }
        }

        public List<BezierPoint3D> KeyPoints
        {
            get
            {
                return this.keyPoints;
            }
        }

        public int KeyPointsCount
        {
            get
            {
                return this.KeyPoints.Count;
            }
        }

        // Public Methods

        /// <summary>
        /// Adds a key point at the end of the curve
        /// </summary>
        /// <returns>The new key point</returns>
        public BezierPoint3D AddKeyPoint()
        {
            return this.AddKeyPointAt(this.KeyPointsCount);
        }

        /// <summary>
        /// Add a key point at a specified index
        /// </summary>
        /// <param name="index">The index at which the key point will be added</param>
        /// <returns>The new key point</returns>
        public BezierPoint3D AddKeyPointAt(int index)
        {
            BezierPoint3D newPoint = new GameObject("Point " + this.KeyPoints.Count, typeof(BezierPoint3D)).GetComponent<BezierPoint3D>();
            newPoint.Curve = this;
            newPoint.transform.parent = this.transform;
            newPoint.transform.localRotation = Quaternion.identity;

            if (this.KeyPointsCount == 0 || this.KeyPointsCount == 1)
            {
                newPoint.LocalPosition = Vector3.zero;
            }
            else
            {
                if (index == 0)
                {
                    newPoint.Position = (this.KeyPoints[0].Position - this.KeyPoints[1].Position).normalized + this.KeyPoints[0].Position;
                }
                else if (index == this.KeyPointsCount)
                {
                    newPoint.Position = (this.KeyPoints[index - 1].Position - this.KeyPoints[index - 2].Position).normalized + this.KeyPoints[index - 1].Position;
                }
                else
                {
                    newPoint.Position = BezierCurve3D.GetPointOnCubicCurve(0.5f, this.KeyPoints[index - 1], this.KeyPoints[index]);
                }
            }

            this.KeyPoints.Insert(index, newPoint);

            ClearLUTCache();
            return newPoint;
        }

        /// <summary>
        /// Removes a key point at a specified index
        /// </summary>
        /// <param name="index">The index of the key point that will be removed</param>
        /// <returns>true - if the point was removed, false - otherwise</returns>
        public bool RemoveKeyPointAt(int index)
        {
            if (this.KeyPointsCount < 2)
            {
                return false;
            }

            var point = this.KeyPoints[index];
            this.KeyPoints.RemoveAt(index);

#if UNITY_EDITOR
            if (Application.isPlaying)
                Destroy(point.gameObject);
            else
                DestroyImmediate(point.gameObject);
#else

            Destroy(point.gameObject);

#endif
            ClearLUTCache();
            return true;
        }

        /// <summary>
        /// Evaluates a position along the curve at a specified normalized time [0, 1]
        /// </summary>
        /// <param name="time">The normalized length at which we want to get a position [0, 1]</param>
        /// <returns>The evaluated Vector3 position</returns>
        public Vector3 GetPoint(float time)
        {
            // The evaluated points is between these two points
            BezierPoint3D startPoint;
            BezierPoint3D endPoint;
            float timeRelativeToSegment;

            this.GetCubicSegment(time, out startPoint, out endPoint, out timeRelativeToSegment);

            return BezierCurve3D.GetPointOnCubicCurve(timeRelativeToSegment, startPoint, endPoint);
        }

        public Quaternion GetRotation(float time, Vector3 up)
        {
            BezierPoint3D startPoint;
            BezierPoint3D endPoint;
            float timeRelativeToSegment;

            this.GetCubicSegment(time, out startPoint, out endPoint, out timeRelativeToSegment);

            return BezierCurve3D.GetRotationOnCubicCurve(timeRelativeToSegment, up, startPoint, endPoint);
        }

        public Vector3 GetTangent(float time)
        {
            BezierPoint3D startPoint;
            BezierPoint3D endPoint;
            float timeRelativeToSegment;

            this.GetCubicSegment(time, out startPoint, out endPoint, out timeRelativeToSegment);

            return BezierCurve3D.GetTangentOnCubicCurve(timeRelativeToSegment, startPoint, endPoint);
        }

        public Vector3 GetBinormal(float time, Vector3 up)
        {
            BezierPoint3D startPoint;
            BezierPoint3D endPoint;
            float timeRelativeToSegment;

            this.GetCubicSegment(time, out startPoint, out endPoint, out timeRelativeToSegment);

            return BezierCurve3D.GetBinormalOnCubicCurve(timeRelativeToSegment, up, startPoint, endPoint);
        }

        public Vector3 GetNormal(float time, Vector3 up)
        {
            BezierPoint3D startPoint;
            BezierPoint3D endPoint;
            float timeRelativeToSegment;

            this.GetCubicSegment(time, out startPoint, out endPoint, out timeRelativeToSegment);

            return BezierCurve3D.GetNormalOnCubicCurve(timeRelativeToSegment, up, startPoint, endPoint);
        }

        public float GetApproximateLength()
        {
            float length = 0;
            int subCurveSampling = (this.Sampling / (this.KeyPointsCount - 1)) + 1;
            for (int i = 0; i < this.KeyPointsCount - 1; i++)
            {
                length += BezierCurve3D.GetApproximateLengthOfCubicCurve(this.KeyPoints[i], this.KeyPoints[i + 1], subCurveSampling);
            }

            return length;
        }

        public void GetCubicSegment(float time, out BezierPoint3D startPoint, out BezierPoint3D endPoint, out float timeRelativeToSegment)
        {
            startPoint = null;
            endPoint = null;
            timeRelativeToSegment = 0f;

            float subCurvePercent = 0f;
            float totalPercent = 0f;
            float approximateLength = this.GetApproximateLength();
            int subCurveSampling = (this.Sampling / (this.KeyPointsCount - 1)) + 1;

            for (int i = 0; i < this.KeyPointsCount - 1; i++)
            {
                subCurvePercent = BezierCurve3D.GetApproximateLengthOfCubicCurve(this.KeyPoints[i], this.KeyPoints[i + 1], subCurveSampling) / approximateLength;
                if (subCurvePercent + totalPercent > time)
                {
                    startPoint = this.KeyPoints[i];
                    endPoint = this.KeyPoints[i + 1];

                    break;
                }

                totalPercent += subCurvePercent;
            }

            if (endPoint == null)
            {
                // If the evaluated point is very near to the end of the curve we are in the last segment
                startPoint = this.KeyPoints[this.KeyPointsCount - 2];
                endPoint = this.KeyPoints[this.KeyPointsCount - 1];

                // We remove the percentage of the last sub-curve
                totalPercent -= subCurvePercent;
            }

            timeRelativeToSegment = (time - totalPercent) / subCurvePercent;
        }

        /// <summary>
        /// Finds the on-curve point closest to the specific off-curve point
        /// </summary>
        /// <param name="point">position off curve</param>
        /// <returns>time on curve</returns>
        public float Project(Vector3 point, int steps = 100)
        {
            //Get Lut table
            LUTPoint[] LUT = getLUT(steps);

            LUTPoint closestPoint = ClosestLUTToPoint(point, LUT, out var distance);
            LUTPoint nextPoint; 

            if (closestPoint.index == 0)
            {
                nextPoint = LUT[1];
            }
            else if (closestPoint.index == LUT.Length - 1)
            {
                nextPoint = LUT[LUT.Length - 2];
            }
            else if (Vector3.Distance(LUT[closestPoint.index + 1].position, point) < Vector3.Distance(LUT[closestPoint.index - 1].position, point))
            {
                nextPoint = LUT[closestPoint.index + 1];
            }
            else
            {
                nextPoint = LUT[closestPoint.index - 1];
            }


            Vector3 normal = nextPoint.position - closestPoint.position;
            Vector3 directionToPoint = point - closestPoint.position;

            float ratio = Vector3.Dot(directionToPoint, normal) / normal.sqrMagnitude;
            return Mathf.Lerp(closestPoint.time, nextPoint.time, ratio);
        }

        public LUTPoint[] getLUT(int steps = 100)
        {
            //catch negative steps
            if (steps <= 0)
            {
                throw new System.ArgumentOutOfRangeException("steps", steps, "Steps must be 1 or higher");
            }


            LUTPoint[] LUT;
            if (m_cachedLUTs.TryGetValue(steps, out LUT))
            {
                return LUT;
            }

            LUT = new LUTPoint[steps];
            if (steps == 1)
                LUT[0] = new LUTPoint { index = 0, position = GetPoint(0.5f), time = 0.5f };
            else
            {
                for (int i = 0; i < steps; i++)
                {
                    float time = (float)i / (steps - 1);
                    LUT[i] = new LUTPoint { index = i, position = GetPoint(time), time = time };
                }
            }

            m_cachedLUTs.Add(steps, LUT);
            return LUT;
        }

        public LUTPoint ClosestLUTToPoint(Vector3 point, LUTPoint[] LUT, out float distance)
        {

            if (LUT.Length == 0)
                throw new System.ArgumentNullException("LUT", "LUT array must not be empty");

            float smallest_dist = Vector3.Distance(point, LUT[0].position);
            LUTPoint retVal = LUT[0];
            float temp_distance;

            //skip first LUT
            for (int i = 1; i < LUT.Length; i++)
            {
                temp_distance = Vector3.Distance(point, LUT[i].position);
                if (temp_distance < smallest_dist)
                {
                    smallest_dist = temp_distance;
                    retVal = LUT[i];
                }
            }

            distance = smallest_dist;
            return retVal;
        }

        public void ClearLUTCache()
        {
            m_cachedLUTs.Clear();
        }

        public static Vector3 GetPointOnCubicCurve(float time, BezierPoint3D startPoint, BezierPoint3D endPoint)
        {
            return GetPointOnCubicCurve(time, startPoint.Position, endPoint.Position, startPoint.RightHandlePosition, endPoint.LeftHandlePosition);
        }

        public static Vector3 GetPointOnCubicCurve(float time, Vector3 startPosition, Vector3 endPosition, Vector3 startTangent, Vector3 endTangent)
        {
            float t = time;
            float u = 1f - t;
            float t2 = t * t;
            float u2 = u * u;
            float u3 = u2 * u;
            float t3 = t2 * t;

            Vector3 result =
                (u3) * startPosition +
                (3f * u2 * t) * startTangent +
                (3f * u * t2) * endTangent +
                (t3) * endPosition;

            return result;
        }

        public static Quaternion GetRotationOnCubicCurve(float time, Vector3 up, BezierPoint3D startPoint, BezierPoint3D endPoint)
        {
            return GetRotationOnCubicCurve(time, up, startPoint.Position, endPoint.Position, startPoint.RightHandlePosition, endPoint.LeftHandlePosition);
        }

        public static Quaternion GetRotationOnCubicCurve(float time, Vector3 up, Vector3 startPosition, Vector3 endPosition, Vector3 startTangent, Vector3 endTangent)
        {
            Vector3 tangent = GetTangentOnCubicCurve(time, startPosition, endPosition, startTangent, endTangent);
            Vector3 normal = GetNormalOnCubicCurve(time, up, startPosition, endPosition, startTangent, endTangent);

            return Quaternion.LookRotation(tangent, normal);
        }

        public static Vector3 GetTangentOnCubicCurve(float time, BezierPoint3D startPoint, BezierPoint3D endPoint)
        {
            return GetTangentOnCubicCurve(time, startPoint.Position, endPoint.Position, startPoint.RightHandlePosition, endPoint.LeftHandlePosition);
        }

        public static Vector3 GetTangentOnCubicCurve(float time, Vector3 startPosition, Vector3 endPosition, Vector3 startTangent, Vector3 endTangent)
        {
            float t = time;
            float u = 1f - t;
            float u2 = u * u;
            float t2 = t * t;

            Vector3 tangent =
                (-u2) * startPosition +
                (u * (u - 2f * t)) * startTangent -
                (t * (t - 2f * u)) * endTangent +
                (t2) * endPosition;

            return tangent.normalized;
        }

        public static Vector3 GetBinormalOnCubicCurve(float time, Vector3 up, BezierPoint3D startPoint, BezierPoint3D endPoint)
        {
            return GetBinormalOnCubicCurve(time, up, startPoint.Position, endPoint.Position, startPoint.RightHandlePosition, endPoint.LeftHandlePosition);
        }

        public static Vector3 GetBinormalOnCubicCurve(float time, Vector3 up, Vector3 startPosition, Vector3 endPosition, Vector3 startTangent, Vector3 endTangent)
        {
            Vector3 tangent = GetTangentOnCubicCurve(time, startPosition, endPosition, startTangent, endTangent);
            Vector3 binormal = Vector3.Cross(up, tangent);

            return binormal.normalized;
        }

        public static Vector3 GetNormalOnCubicCurve(float time, Vector3 up, BezierPoint3D startPoint, BezierPoint3D endPoint)
        {
            return GetNormalOnCubicCurve(time, up, startPoint.Position, endPoint.Position, startPoint.RightHandlePosition, endPoint.LeftHandlePosition);
        }

        public static Vector3 GetNormalOnCubicCurve(float time, Vector3 up, Vector3 startPosition, Vector3 endPosition, Vector3 startTangent, Vector3 endTangent)
        {
            Vector3 tangent = GetTangentOnCubicCurve(time, startPosition, endPosition, startTangent, endTangent);
            Vector3 binormal = GetBinormalOnCubicCurve(time, up, startPosition, endPosition, startTangent, endTangent);
            Vector3 normal = Vector3.Cross(tangent, binormal);

            return normal.normalized;
        }

        public static float GetApproximateLengthOfCubicCurve(BezierPoint3D startPoint, BezierPoint3D endPoint, int sampling)
        {
            return GetApproximateLengthOfCubicCurve(startPoint.Position, endPoint.Position, startPoint.RightHandlePosition, endPoint.LeftHandlePosition, sampling);
        }

        public static float GetApproximateLengthOfCubicCurve(Vector3 startPosition, Vector3 endPosition, Vector3 startTangent, Vector3 endTangent, int sampling)
        {
            float length = 0f;
            Vector3 fromPoint = GetPointOnCubicCurve(0f, startPosition, endPosition, startTangent, endTangent);

            for (int i = 0; i < sampling; i++)
            {
                float time = (i + 1) / (float)sampling;
                Vector3 toPoint = GetPointOnCubicCurve(time, startPosition, endPosition, startTangent, endTangent);
                length += Vector3.Distance(fromPoint, toPoint);
                fromPoint = toPoint;
            }

            return length;
        }

        // Protected Methods

        protected virtual void OnDrawGizmos()
        {
            if (this.KeyPointsCount > 1)
            {
                // Draw the curve
                Vector3 fromPoint = this.GetPoint(0f);

                for (int i = 0; i < this.Sampling; i++)
                {
                    float time = (i + 1) / (float)this.Sampling;
                    Vector3 toPoint = this.GetPoint(time);

                    // Draw segment
                    Gizmos.color = this.curveColor;
                    Gizmos.DrawLine(fromPoint, toPoint);

                    fromPoint = toPoint;
                }

                // Draw the start and the end of the curve indicators
                Gizmos.color = this.startPointColor;
                Gizmos.DrawSphere(this.KeyPoints[0].Position, 0.05f);

                Gizmos.color = this.endPointColor;
                Gizmos.DrawSphere(this.KeyPoints[this.KeyPointsCount - 1].Position, 0.05f);

                // Draw the point at the normalized time
                Vector3 point = this.GetPoint(this.normalizedTime);
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(point, 0.025f);

                Vector3 tangent = this.GetTangent(this.normalizedTime);
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(point, point + tangent / 2f);

                Vector3 binormal = this.GetBinormal(this.normalizedTime, Vector3.up);
                Gizmos.color = Color.red;
                Gizmos.DrawLine(point, point + binormal / 2f);

                Vector3 normal = this.GetNormal(this.normalizedTime, Vector3.up);
                Gizmos.color = Color.green;
                Gizmos.DrawLine(point, point + normal / 2f);
            }
        }
    }
}
