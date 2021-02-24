using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using Unity.Transforms;
using Unity.Rendering;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.SceneManagement;
using System;

struct Cell : IComponentData
{
    public int cellId;
}

public class LifeSystem : SystemBase
{
    public static string[] rules;
    public string rule;
    public int size = 20;

    private NativeArray<int> board;
    private NativeArray<int> next;
    private NativeArray<Entity> entities;

    private RenderMesh cubeMesh;
    public Material material;

    EntityArchetype cubeArchetype;

    EntityManager entityManager;

    internal static readonly AABB OutOfBounds = new AABB
    {
        Center = new float3(-1000000, -1000000, -1000000),
        Extents = new float3(0, 0, 0),
    };

    internal static readonly float3 PositionOutOfBounds = new float3(-1000000, -1000000, -1000000);

    private void Randomize()
    {
        for (int slice = 0; slice < size; slice++)
        {
            for (int row = 0; row < size; row++)
            {
                for (int col = 0; col < size; col++)
                {
                    float dice = UnityEngine.Random.Range(0.0f, 1.0f);
                    if (dice > 0.5f)
                    {
                        Set(slice, row, col, 255);
                    }
                    else
                    {
                        Set(slice, row, col, 0);
                    }
                }
            }
        }
    }

    private void Set(int slice, int row, int col, int val)
    {
        int cell = ToCell(size, slice, row, col);
        board[cell] = val;

    }

    protected override void OnCreate()
    {
        board = new NativeArray<int>((int)Mathf.Pow(size, 3), Allocator.Persistent);
        next = new NativeArray<int>((int)Mathf.Pow(size, 3), Allocator.Persistent);
        entities = new NativeArray<Entity>((int)Mathf.Pow(size, 3), Allocator.Persistent);

        //Enabled = false;

        entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;

        CreateArchetype();

        CreateEntities();


        InitialState();
    }

    private void CreateEntities()
    {
        entityManager.CreateEntity(cubeArchetype, entities);
    }

    private void CreateArchetype()
    {
        cubeArchetype = entityManager.CreateArchetype(
                    typeof(Translation),
                    typeof(LocalToWorld),
                    typeof(RenderBounds),
                    typeof(Cell),
                    typeof(RenderMesh)

        );

        Material material = Resources.Load<Material>("LifeMaterial");
        GameObject c = Resources.Load<GameObject>("Cube 1");
        Mesh mesh = c.GetComponent<MeshFilter>().sharedMesh;
        cubeMesh = new RenderMesh
        {
            mesh = mesh,
            material = material
        };
    }

    private void InitialState()
    {
        Randomize();

        /*for(int col = 0 ; col < size ; col ++)
        {
            Set(0, size / 2, col, 255);
            Set(0, (size / 2) + 1, col, 255);
        }
        */

    }

    protected override void OnDestroy()
    {
        board.Dispose();
        next.Dispose();
        entities.Dispose();
    }

    float timePassed = 0;
    int generation = 0;
    bool populated = false;

    public static int ToCell(int size, int slice, int row, int col)
    {
        return (slice * size * size) + (row * size) + col;
    }

    public static int Get(ref NativeArray<int> board, int size, int slice, int row, int col)
    {
        if (row < 0 || row >= size || col < 0 || col >= size || slice < 0 || slice >= size)
        {
            return 0;
        }
        return (board[ToCell(size, slice, row, col)]);
    }

    public static void Set(ref NativeArray<int> board, int size, int slice, int row, int col, int val, int i)
    {
        int cell = ToCell(size, slice, row, col);
        board[cell] = val;
    }

    private static int CountNeighbours(ref NativeArray<int> board, int size, int slice, int row, int col)
    {
        int count = 0;

        for (int s = slice - 1; s <= slice + 1; s++)
        {
            for (int r = row - 1; r <= row + 1; r++)
            {
                for (int c = col - 1; c <= col + 1; c++)
                {
                    if (!(r == row && c == col && s == slice))
                    {
                        if (Get(ref board, size, s, r, c) > 0)
                        {
                            count++;
                        }
                    }
                }
            }
        }
        return count;
    }

    protected override void OnUpdate()
    {
        timePassed += Time.DeltaTime;

        NativeArray<int> board = this.board;
        NativeArray<int> next = this.next;
        int size = this.size;
        
        if (!populated)
        {
            populated = true;
            JobHandle popHandle = Entities.ForEach((int entityInQueryIndex, ref Cell c, ref Translation p) =>
            {
                int slice = (entityInQueryIndex / (size * size));
                int row = (entityInQueryIndex - (slice * size * size)) / (size);
                int col = (entityInQueryIndex - (row * size)) - (slice * size * size);
                c.cellId = entityInQueryIndex;
                if (board[entityInQueryIndex] > 0)
                {
                    p.Value = new float3(slice, row, col);
                }
                else
                {
                    p.Value = new float3(-1000000, -1000000, -1000000);
                }
            })
            .ScheduleParallel(Dependency);
            Dependency = JobHandle.CombineDependencies(Dependency, popHandle);
        }
        
        if (timePassed > 2.0f)
        {
            generation++;
            timePassed = 0;

            var lifeHandle = Entities
                .WithNativeDisableParallelForRestriction(board)
                .ForEach((int entityInQueryIndex, ref Cell c, ref Translation p) =>
                {
                    int i = entityInQueryIndex;
                    int slice = (i / (size * size));
                    int row = (i - (slice * size * size)) / (size);
                    int col = (i - (row * size)) - (slice * size * size);
                    int count = CountNeighbours(ref board, size, slice, row, col);
                    if (Get(ref board, size, slice, row, col) > 0)
                    {
                        if (count == 4 || count == 5)
                        {

                            Set(ref next, size, slice, row, col, 255, i);
                        }
                        else
                        {
                            Set(ref next, size, slice, row, col, 0, i);
                            p.Value = PositionOutOfBounds;
                        }
                    }
                    else if (count == 5)
                    {
                        Set(ref next, size, slice, row, col, 255, i);
                    }
                    else
                    {
                        Set(ref next, size, slice, row, col, 0, i);
                        p.Value = PositionOutOfBounds;
                    }
                })
                .ScheduleParallel(Dependency);        
            Dependency = JobHandle.CombineDependencies(Dependency, lifeHandle);        

            var cnJob = new CopyNextToBoard()
            {
                next = this.next,
                board = this.board
            };

            var cnHandle = cnJob.Schedule(size * size * size, 1, Dependency);
            Dependency = JobHandle.CombineDependencies(Dependency, cnHandle); 
        }
    }

    [BurstCompile]
    struct CopyNextToBoard : IJobParallelFor
    {
        public NativeArray<int> board;

        public NativeArray<int> next;

        public void Execute(int i)
        {
            board[i] = next[i];
        }
    }
}