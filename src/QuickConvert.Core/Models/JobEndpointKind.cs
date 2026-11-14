namespace QuickConvert.Core.Models;

/// <summary>Whether one end of a job is a file or a folder. Existing jobs are File→File by default.</summary>
public enum JobEndpointKind { File = 0, Directory = 1 }
