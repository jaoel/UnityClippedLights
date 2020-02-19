using UnityEngine;
using System.Collections.Generic;

namespace ClippedLights {
    public struct BoundingVolumeFace {
        public Vector3 center;
        public int[] pointIndices;
        public bool included;
    }

    public class ClippedLightBoundingVolumeGeometry {
        public struct PlaneIntersectionPoint {
            public Vector3 point;
            public int[] planeIndices;

            public bool IsPartOfPlane(int planeIndex) {
                for (int i = 0; i < planeIndices.Length; i++) {
                    if (planeIndices[i] == planeIndex) {
                        return true;
                    }
                }
                return false;
            }
        }

        public Vector3[] points = new Vector3[0];
        public BoundingVolumeFace[] faces = new BoundingVolumeFace[0];

        private Vector4[] cachedPlanes = new Vector4[6];

        public void Calculate(ClippedLight light) {
            bool dirty = false;
            for (int i = 0; i < light.planes.Length; i++) {
                if (light.planes[i] != cachedPlanes[i]) {
                    dirty = true;
                    break;
                }
            }

            if (dirty) {
                CalculateBoundingVolume(light);
                System.Array.Copy(light.planes, cachedPlanes, light.planes.Length);
            }
        }

        private bool TryGetPlaneIntersection(Vector4 a, Vector4 b, Vector4 c, out Vector3 point) {
            float d = Vector3.Dot(a, Vector3.Cross(b, c));
            if (d != 0f) {
                point = (-a.w * Vector3.Cross(b, c) - b.w * Vector3.Cross(c, a) - c.w * Vector3.Cross(a, b)) / d;
                return true;
            }

            point = Vector3.zero;
            return false;
        }

        private void CalculateBoundingVolume(ClippedLight light) {
            int planeCount = light.planes.Length;

            // Get a list of all planes where planes outside of the light's radius are set to the radius/bounds of the light.
            Vector4[] boundedPlanes = new Vector4[planeCount];
            for (int i = 0; i < planeCount; i++) {
                Vector4 plane = light.planes[i];
                plane.w = Mathf.Clamp(plane.w, -light.range, light.range);
                boundedPlanes[i] = plane;
            }

            // Get all points where 3 planes intersect
            List<PlaneIntersectionPoint> allIntersections = new List<PlaneIntersectionPoint>();
            for (int a = 0; a < planeCount; a++) {
                Vector4 planeA = boundedPlanes[a];
                for (int b = 0; b < planeCount; b++) {
                    if (a == b) break;
                    Vector4 planeB = boundedPlanes[b];
                    for (int c = 0; c < planeCount; c++) {
                        if (a == c || b == c) break;
                        Vector4 planeC = boundedPlanes[c];
                        if (TryGetPlaneIntersection(planeA, planeB, planeC, out Vector3 point)) {
                            allIntersections.Add(new PlaneIntersectionPoint() {
                                point = point,
                                planeIndices = new[] { a, b, c }
                            });
                        }
                    }
                }
            }

            // Filter intersection points and only keep points that are inside the bounding volume
            List<int> visibleIntersectionIndices = new List<int>();
            for (int i = 0; i < allIntersections.Count; i++) {
                bool visible = true;
                for (int p = 0; p < planeCount; p++) {
                    Vector4 plane = light.planes[p];
                    Vector4 point = allIntersections[i].point;
                    point.w = 1f;
                    if (Vector4.Dot(plane, point) < 0f && !allIntersections[i].IsPartOfPlane(p)) {
                        visible = false;
                        break;
                    }
                }
                if (visible) {
                    visibleIntersectionIndices.Add(i);
                }
            }

            // Add all visible points to the output list
            List<Vector3> pointsList = new List<Vector3>();
            for (int i = 0; i < visibleIntersectionIndices.Count; i++) {
                int visibleIndex = visibleIntersectionIndices[i];
                pointsList.Add(allIntersections[visibleIndex].point);
            }
            points = pointsList.ToArray();

            // Calculate bounding volume face data
            List<BoundingVolumeFace> facesList = new List<BoundingVolumeFace>();
            for (int i = 0; i < planeCount; i++) {
                List<int> pointIndices = new List<int>();
                for (int p = 0; p < visibleIntersectionIndices.Count; p++) {
                    int intersectionIndex = visibleIntersectionIndices[p];
                    if (allIntersections[intersectionIndex].planeIndices[0] == i
                        || allIntersections[intersectionIndex].planeIndices[1] == i
                        || allIntersections[intersectionIndex].planeIndices[2] == i) {
                        pointIndices.Add(p);
                    }
                }

                Vector3 centerPos = Vector3.zero;
                bool included;
                if (pointIndices.Count > 0) {
                    // Find the center point as the average of all points intersecting this plane.
                    for (int p = 0; p < pointIndices.Count; p++) {
                        centerPos += pointsList[pointIndices[p]];
                    }
                    centerPos /= pointIndices.Count;

                    // Sort the points by angle
                    pointIndices.Sort((first, second) => {
                        Vector3 up = light.planes[i];
                        Vector3 forward = pointsList[0] - centerPos;
                        Vector3 toFirst = pointsList[first] - centerPos;
                        Vector3 toSecond = pointsList[second] - centerPos;
                        float firstAngle = Vector3.SignedAngle(forward, toFirst, up);
                        float secondAngle = Vector3.SignedAngle(forward, toSecond, up);
                        return firstAngle.CompareTo(secondAngle);
                    });

                    included = true;
                } else {
                    // This plane is not part of the bounding box, so we will set the center point for this face
                    // to be the vector from the light center in the direction of the plane normal.
                    centerPos = -light.planes[i] * light.planes[i].w;


                    float closestDist = float.MaxValue;
                    foreach (Vector3 point in points) {
                        float dot = Vector4.Dot(light.planes[i], new Vector4(point.x, point.y, point.z, 1f));
                        if (dot < closestDist) {
                            centerPos = point;
                            closestDist = dot;
                        }
                    }

                    included = false;
                }

                facesList.Add(new BoundingVolumeFace() {
                    center = centerPos,
                    pointIndices = pointIndices.ToArray(),
                    included = included
                });
            }
            faces = facesList.ToArray();
        }
    }
}
