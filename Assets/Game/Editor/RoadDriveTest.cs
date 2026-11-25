using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Test tool (in Play): drives a car along every traffic lane and junction turn of the city and reports where one ever goes
/// through the road. A batch of cars runs at once, on physics stepped by hand; each car's own colliders and suspension carry it,
/// only its speed and direction are set. Besides the lane itself, runs offset to the left and right cover the road's width.
/// </summary>
public static class RoadDriveTest
{
    class Run { public List<Vector3> points; public float offset; public string name; }
    class Car { public GameObject go; public Rigidbody body; public Run run; public int at; public float stuck; public int steps; }

    static readonly Queue<Run> queue = new Queue<Run>();
    static readonly List<Car> cars = new List<Car>();
    static readonly List<string> failures = new List<string>();
    static string output;
    static GameObject prefab;
    static float speed, metres;
    static int total, finished, stuckCount;

    public static string Start(string prefabPath, string outputFile, float metresPerSecond, int batch)
    {
        if (!Application.isPlaying) return "Enter Play first.";
        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var manager = Object.FindFirstObjectByType<TSMainManager>(FindObjectsInactive.Include);
        if (prefab == null || manager == null) return "Missing car or lanes.";
        output = outputFile;
        speed = metresPerSecond;
        metres = 0f;
        finished = stuckCount = 0;
        queue.Clear();
        cars.Clear();
        failures.Clear();
        for (int l = 0; l < manager.lanes.Length; l++)
        {
            var lane = manager.lanes[l];
            var points = lane.points.Select(p => p.point).ToList();
            if (points.Count > 1)
                foreach (float offset in new[] { 0f, -2.2f, 2.2f }) queue.Enqueue(new Run { points = points, offset = offset, name = "lane " + l + " offset " + offset });
            for (int c = 0; c < lane.connectors.Length; c++)
            {
                var turn = lane.connectors[c].points.Select(p => p.point).ToList();
                if (turn.Count > 1) queue.Enqueue(new Run { points = turn, name = "lane " + l + " turn " + c });
            }
        }
        total = queue.Count;
        File.WriteAllText(output, "runs " + total + "\n");
        // Nothing else moves: the city's own cars and people are out of the way for the test.
        foreach (var body in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) body.gameObject.SetActive(false);
        Physics.simulationMode = SimulationMode.Script;
        for (int i = 0; i < batch; i++) Next(null);
        for (int a = 0; a < cars.Count; a++)
            for (int b = a + 1; b < cars.Count; b++)
                foreach (var x in cars[a].go.GetComponentsInChildren<Collider>())
                    foreach (var y in cars[b].go.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(x, y);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        return "started: " + total + " runs";
    }

    static Vector3 Point(Run run, int i)
    {
        Vector3 p = run.points[i];
        if (run.offset == 0f) return p;
        Vector3 d = run.points[Mathf.Min(i + 1, run.points.Count - 1)] - run.points[Mathf.Max(i - 1, 0)];
        d.y = 0f;
        return p + Vector3.Cross(Vector3.up, d.normalized) * run.offset;
    }

    // The next run for this car (a new car when there is none).
    static bool Next(Car car)
    {
        if (queue.Count == 0)
        {
            if (car != null) { Object.Destroy(car.go); cars.Remove(car); }
            return false;
        }
        var run = queue.Dequeue();
        if (car == null)
        {
            var go = Object.Instantiate(prefab);
            foreach (var script in go.GetComponentsInChildren<MonoBehaviour>()) if (script != null) script.enabled = false;
            car = new Car { go = go, body = go.GetComponent<Rigidbody>() };
            car.body.isKinematic = false;
            cars.Add(car);
        }
        car.run = run;
        car.at = 1;
        car.stuck = 0f;
        car.steps = 0;
        Place(car, 0);
        return true;
    }

    static void Place(Car car, int i)
    {
        Vector3 p = Point(car.run, i), d = Point(car.run, Mathf.Min(i + 1, car.run.points.Count - 1)) - p;
        if (d.sqrMagnitude < 0.01f) d = Vector3.forward;
        car.body.position = car.go.transform.position = p + Vector3.up * 0.7f;
        car.body.rotation = car.go.transform.rotation = Quaternion.LookRotation(d.normalized);
        car.body.linearVelocity = d.normalized * speed;
        car.body.angularVelocity = Vector3.zero;
    }

    static void Tick()
    {
        if (!Application.isPlaying) { EditorApplication.update -= Tick; return; }
        const float dt = 1f / 60f;
        double until = EditorApplication.timeSinceStartup + 0.25;
        while (EditorApplication.timeSinceStartup < until && cars.Count > 0)
        {
            foreach (var car in cars)
            {
                Vector3 to = Point(car.run, car.at) - car.body.position;
                float y = car.body.linearVelocity.y;
                to.y = 0f;
                if (to.sqrMagnitude < 0.01f) continue;
                Vector3 v = to.normalized * speed;
                car.body.linearVelocity = new Vector3(v.x, Mathf.Clamp(y, -20f, 8f), v.z);
                Vector3 up = Vector3.Slerp(car.body.rotation * Vector3.up, Vector3.up, 0.02f);
                car.body.MoveRotation(Quaternion.Slerp(car.body.rotation, Quaternion.LookRotation(Vector3.ProjectOnPlane(to, up).normalized, up), 0.2f));
                car.body.angularVelocity = Vector3.zero;
            }
            Physics.Simulate(dt);
            for (int c = cars.Count - 1; c >= 0; c--)
            {
                var car = cars[c];
                Vector3 target = Point(car.run, car.at);
                Vector3 flat = target - car.body.position;
                flat.y = 0f;
                car.steps++;
                // Under the lane by more than a car's ground clearance: through the road.
                Vector3 a = Point(car.run, car.at - 1);
                float span = Vector3.Distance(new Vector3(a.x, 0f, a.z), new Vector3(target.x, 0f, target.z));
                float laneY = Mathf.Lerp(target.y, a.y, span > 0.01f ? Mathf.Clamp01(flat.magnitude / span) : 0f);
                if (car.body.position.y < laneY - 1.2f)
                {
                    failures.Add(car.run.name + " fell at " + car.body.position.ToString("F1") + " lane y " + laneY.ToString("F1"));
                    File.AppendAllText(output, failures[failures.Count - 1] + "\n");
                    car.at = Mathf.Min(car.at + 3, car.run.points.Count - 1);
                    Place(car, car.at - 1);
                    if (car.at >= car.run.points.Count - 1) { finished++; Next(car); }
                    continue;
                }
                // Blocked by a kerb, a pole or a wall on an offset run: not what is tested here, carried on past it.
                car.stuck += dt;
                if (flat.magnitude < Mathf.Max(2f, speed * dt * 1.5f))
                {
                    metres += span;
                    car.stuck = 0f;
                    if (++car.at >= car.run.points.Count) { finished++; Next(car); }
                }
                else if (car.stuck > span / speed + 2f)
                {
                    stuckCount++;
                    car.stuck = 0f;
                    if (++car.at >= car.run.points.Count) { finished++; Next(car); }
                    else Place(car, car.at - 1);
                }
            }
        }
        EditorApplication.QueuePlayerLoopUpdate();
        if (cars.Count > 0) return;
        EditorApplication.update -= Tick;
        Physics.simulationMode = SimulationMode.FixedUpdate;
        File.AppendAllText(output, "DONE runs " + finished + "/" + total + ", " + (metres / 1000f).ToString("F1") + " km, falls " + failures.Count + ", blocked " + stuckCount + "\n");
    }

    public static string Progress() => finished + "/" + total + " runs, " + (metres / 1000f).ToString("F1") + " km, falls " + failures.Count + ", blocked " + stuckCount;
}
