// MSBuildWorkspace loads run out of process; concurrent loads of unrestored and restored projects
// made restored ones come back without framework references.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
