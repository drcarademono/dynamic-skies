#ifndef MOON_INCLUDE
    #define MOON_INCLUDE

    //a rotation matrix to rotate and thing in world space
    float3 RotateWorldPosition(float3 position, float3 axis)
    {
        float3 rot = axis;
        float3x3 rotMat = float3x3(cos(rot.y) * cos(rot.z), -cos(rot.y) * sin(rot.z),
                                sin(rot.y), (cos(rot.x) * sin(rot.z)) + (sin(rot.x) * sin(rot.y) * cos(rot.z)),
                                (cos(rot.x) * cos(rot.z)) - (sin(rot.x) * sin(rot.y) * sin(rot.z)),
                                -sin(rot.x) * cos(rot.y), (sin(rot.x) * sin(rot.z)) - (cos(rot.x) * sin(rot.y) * cos(rot.z)),
                                (sin(rot.x) * cos(rot.z)) + (cos(rot.x) * sin(rot.y) * sin(rot.z)), cos(rot.x) * cos(rot.y));
        float3 rotPos = mul(rotMat, position);
        return rotPos;
    }

    //this is using the equation of an ellipse. The angle is time * speed, or what ever angle you want to sample at
    float3 ElipsePosition(float2 MajMinAxis, float angle)
    {
        float3 orbitPos;
        orbitPos.x = (MajMinAxis.x * cos(angle));
        orbitPos.y = 0;
        orbitPos.z = (MajMinAxis.y * sin(angle));
        return normalize(orbitPos);
    }

    //this just calls the above function and rotates it by the desired amount
    float3 GetOrbitPosition(float3 orbitOffsetAngles, float2 MajMinAxis, float angle)
    {
        float3 p = ElipsePosition(MajMinAxis, angle);
        p = RotateWorldPosition(p, float3(radians(orbitOffsetAngles.x), radians(orbitOffsetAngles.y), radians(orbitOffsetAngles.z)));
        return p;
    }

    //this lerps between two radius values based on how close the moon is to either the major or minor axis, major in this case is always (1, 0, 0)
    float GetMoonDistance(float Min, float Max, float2 MajMinAxis, float angle)
    {
        float3 pos = ElipsePosition(MajMinAxis, angle);
        float lerpFactor = abs(dot(pos, float3(0, 0, 1)));
        float dist = lerp(Min, Max, smoothstep(0, 1, lerpFactor));
        return dist;
    }

    //sphere tracing from https://www.iquilezles.org/www/articles/intersectors/intersectors.htm
    float SphereIntersect(float3 rayOrigin, float3 rayDirection, float3 spherePos, float sphereRadius)
    {
        float3 originToCenter = rayOrigin - spherePos;
        float b = dot(originToCenter, rayDirection);
        float c = dot(originToCenter, originToCenter) - sphereRadius * sphereRadius;
        float h = b * b - c;
        if (h < 0.0)
        {
            return -1.0;
        }
        h = sqrt(h);
        return -b - h;
    }

    // How to rotate around any axis https://en.wikipedia.org/wiki/Rotation_matrix#Rotation_matrix_from_axis_and_angle
    float3 RotateArbitraryAxis(float3 vec, float angle, float3 axis)
    {
        float rads = radians(angle);
        float3x3 rot = float3x3(float3(cos(rads) + dot(axis.x, axis.x) * (1 - cos(rads)), axis.x * axis.y * (1 - cos(rads)) - axis.z * sin(rads), axis.x * axis.z * (1 - cos(rads)) + axis.y * sin(rads)),
                            float3(axis.y * axis.x * (1 - cos(rads)) + axis.z * sin(rads), cos(rads) + dot(axis.y, axis.y) * (1 - cos(rads)), axis.y * axis.z * (1 - cos(rads)) - axis.x * sin(rads)),
                            float3(axis.z * axis.x * (1 - cos(rads)) - axis.y * sin(rads), axis.z * axis.y * (1 - cos(rads)) + axis.x * sin(rads), cos(rads) + dot(axis.z, axis.z) * (1 - cos(rads))));

        return mul(rot, vec);
    }

#endif