/* CellPath.cs
 * Copyright Eddie Cameron 2012
 * ----------------------------
 * Pathfinding using levels generated by CellGen
 * Provides a grid based path of points to follow, smoothed using raycasting
 */

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent( typeof( CellGen ) )]
public class CellPath : MonoBehaviour 
{
	public LayerMask pathAffectingLayers;
	
	static CellPath instance;
	
	void Awake()
	{
		if ( instance )
			Destroy( this );
		else
			instance = this;
	}
	
	#region Pathing
	public static Vector3[] PathTo( Vector3 fromPoint, Vector3 toPoint )
	{
		fromPoint.y = toPoint.y = 0;
		
		Cell startCell = CellGen.GetCellAt( fromPoint );
		Cell endCell = CellGen.GetCellAt( toPoint );
		
		if ( startCell.x < 0 || endCell.y < 0 )
		{
			Debug.Log( "Invalid path start and/or end" );
			return null;
		}
		
		// A* setup
		var evaluated = new List<Cell>();
		var toEvaluate = new List<Cell>();
		var cameFrom = new Dictionary<Cell, Cell>();
		
		var costTo = new Dictionary<Cell, int>();
		var costFrom = new Dictionary<Cell, float>();
		
		var totalCost = new Dictionary<Cell, float>();
		
		toEvaluate.Add( startCell );
		costTo.Add( startCell, 0 );
		costFrom.Add( startCell, HeuristicDist( startCell, endCell ) );
		totalCost.Add( startCell, costFrom[startCell] );
		
		// A* loop
		while ( toEvaluate.Count > 0 )
		{
			Cell evalPoint = null;
			float lowestCost = float.MaxValue;
			foreach ( var cell in toEvaluate )	// perhaps ove toeval to a heap or something
			{
				float newCost = totalCost[cell];
				if ( newCost < lowestCost )
				{
					lowestCost = newCost;
					evalPoint = cell;
				}
			}
				
			// if reached dest
			if ( evalPoint == endCell )
			{
				var cellPath = PathFrom( cameFrom, evalPoint );
				
				var path = new List<Vector3>();
				path.Add( fromPoint );
				
				for ( int i = 1; i < cellPath.Count - 1; i++ )
					path.Add( cellPath[i].centrePosition );
				
				path.Add( toPoint );
				
				path = SmoothPath( path );
				return path.ToArray();
			}
			
			toEvaluate.Remove( evalPoint );
			evaluated.Add( evalPoint );
			
			var neighbours = new List<Cell>();
			if ( evalPoint.canGoNorth && evalPoint.CellInDirection( Direction.North ).room != 0 )
				neighbours.Add( evalPoint.CellInDirection( Direction.North ) );
			if ( evalPoint.canGoSouth && evalPoint.CellInDirection( Direction.South ).room != 0 )
				neighbours.Add( evalPoint.CellInDirection( Direction.South ) );
			if ( evalPoint.canGoEast && evalPoint.CellInDirection( Direction.East ).room != 0 )
				neighbours.Add( evalPoint.CellInDirection( Direction.East ) );
			if ( evalPoint.canGoWest && evalPoint.CellInDirection( Direction.West ).room != 0 )
				neighbours.Add( evalPoint.CellInDirection( Direction.West ) );
			
			foreach ( Cell newPoint in neighbours )
			{
				if ( evaluated.Contains( newPoint ) )
					continue;
				
				int tentCostTo = costTo[evalPoint] + 1;
				
				if ( !toEvaluate.Contains( newPoint ) )
				{
					toEvaluate.Add( newPoint );
					costTo.Add( newPoint, tentCostTo );
					costFrom.Add( newPoint, HeuristicDist( newPoint, endCell ) );
					totalCost.Add( newPoint, tentCostTo + costFrom[newPoint] );
					cameFrom.Add( newPoint, evalPoint );
				}
				else if ( tentCostTo < costTo[newPoint] )
				{
					costTo[newPoint] = tentCostTo;
					cameFrom[newPoint] = evalPoint;
					totalCost[newPoint] = tentCostTo + costFrom[newPoint];
				}
			}	
		}
		
		Debug.Log( "No path found from " + fromPoint + " to " + toPoint );
		return null;
	}
	
	static float HeuristicDist( Cell fromCell, Cell toCell )
	{
		return ( fromCell.centrePosition - toCell.centrePosition ).magnitude;
	}
	
	static List<Cell> PathFrom( Dictionary<Cell, Cell> cameFrom, Cell curNode )
	{
		Cell lastNode;
		var path = new List<Cell>();
		if ( cameFrom.TryGetValue( curNode, out lastNode ) )
			path.AddRange( PathFrom( cameFrom, lastNode ) );
		
		path.Add( curNode );
		return path;
	}
	
	static List<Vector3> SmoothPath( List<Vector3> path )
	{
		for ( int i = 0; i < path.Count - 1; i++ )
			Debug.DrawLine( path[i] + Vector3.up, path[i + 1] + Vector3.up, Color.green, 5f );
		
		var smoothedPath = new List<Vector3>();
		smoothedPath.Add( path[0] );
		for ( int pathInd = 0; pathInd < path.Count - 1; pathInd++ )
		{
			int nextPoint;
			for ( nextPoint = pathInd + 2; nextPoint < path.Count; nextPoint++ )
				if ( !ClearBetween( smoothedPath[smoothedPath.Count - 1], path[nextPoint] ) )
					break;
				
			nextPoint--;
			Debug.DrawLine( smoothedPath[smoothedPath.Count - 1] + Vector3.up, path[nextPoint] + Vector3.up, Color.blue, 5f );
			
			smoothedPath.Add( path[nextPoint] );
			pathInd = nextPoint;
		}
		return smoothedPath;
	}
	
	static bool ClearBetween( Vector3 fromPoint, Vector3 toPoint )
	{
		fromPoint.y = toPoint.y = 1f;
		return !Physics.Linecast( fromPoint, toPoint, instance.pathAffectingLayers );
	}
	#endregion
	
	void OnDestroy()
	{
		if ( instance == this )
			instance = null;
	}
}