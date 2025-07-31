# MCP Servers Documentation

## ADO

### core_list_project_teams
- **Description**: Retrieve a list of teams for the specified Azure DevOps project.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"mine":{"type":"boolean","description":"If true, only return teams that the authenticated user is a member of."},"top":{"type":"number","description":"The maximum number of teams to return. Defaults to 100."},"skip":{"type":"number","description":"The number of teams to skip for pagination. Defaults to 0."}},"required":["project"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "mine": false|true,
  "top": 42,
  "skip": 42
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <mine> is optional, and is If true, only return teams that the authenticated user is a member of..
   <top> is optional, and is The maximum number of teams to return. Defaults to 100..
   <skip> is optional, and is The number of teams to skip for pagination. Defaults to 0..

```
### core_list_projects
- **Description**: Retrieve a list of projects in your Azure DevOps organization.
- **Input Schema**: {"type":"object","properties":{"stateFilter":{"type":"string","enum":["all","wellFormed","createPending","deleted"],"default":"wellFormed","description":"Filter projects by their state. Defaults to \u0027wellFormed\u0027."},"top":{"type":"number","description":"The maximum number of projects to return. Defaults to 100."},"skip":{"type":"number","description":"The number of projects to skip for pagination. Defaults to 0."},"continuationToken":{"type":"number","description":"Continuation token for pagination. Used to fetch the next set of results if available."},"projectNameFilter":{"type":"string","description":"Filter projects by name. Supports partial matches."}},"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "stateFilter": "<stateFilter>",
  "top": 42,
  "skip": 42,
  "continuationToken": 42,
  "projectNameFilter": "<projectNameFilter>"
}
Where:
   <stateFilter> is optional, and is Filter projects by their state. Defaults to 'wellFormed'..
   <top> is optional, and is The maximum number of projects to return. Defaults to 100..
   <skip> is optional, and is The number of projects to skip for pagination. Defaults to 0..
   <continuationToken> is optional, and is Continuation token for pagination. Used to fetch the next set of results if available..
   <projectNameFilter> is optional, and is Filter projects by name. Supports partial matches..

```
### core_get_identity_ids
- **Description**: Retrieve Azure DevOps identity IDs for a provided search filter.
- **Input Schema**: {"type":"object","properties":{"searchFilter":{"type":"string","description":"Search filter (unique namme, display name, email) to retrieve identity IDs for."}},"required":["searchFilter"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "searchFilter": "<searchFilter>"
}
Where:
   <searchFilter> is Search filter (unique namme, display name, email) to retrieve identity IDs for..

```
### work_list_team_iterations
- **Description**: Retrieve a list of iterations for a specific team in a project.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"team":{"type":"string","description":"The name or ID of the Azure DevOps team."},"timeframe":{"type":"string","enum":["current"],"description":"The timeframe for which to retrieve iterations. Currently, only \u0027current\u0027 is supported."}},"required":["project","team"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "team": "<team>",
  "timeframe": "<timeframe>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <team> is The name or ID of the Azure DevOps team..
   <timeframe> is optional, and is The timeframe for which to retrieve iterations. Currently, only 'current' is supported..

```
### work_create_iterations
- **Description**: Create new iterations in a specified Azure DevOps project.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"iterations":{"type":"array","items":{"type":"object","properties":{"iterationName":{"type":"string","description":"The name of the iteration to create."},"startDate":{"type":"string","description":"The start date of the iteration in ISO format (e.g., \u00272023-01-01T00:00:00Z\u0027). Optional."},"finishDate":{"type":"string","description":"The finish date of the iteration in ISO format (e.g., \u00272023-01-31T23:59:59Z\u0027). Optional."}},"required":["iterationName"],"additionalProperties":false},"description":"An array of iterations to create. Each iteration must have a name and can optionally have start and finish dates in ISO format."}},"required":["project","iterations"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "iterations": null
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <iterations> is An array of iterations to create. Each iteration must have a name and can optionally have start and finish dates in ISO format..

```
### work_assign_iterations
- **Description**: Assign existing iterations to a specific team in a project.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"team":{"type":"string","description":"The name or ID of the Azure DevOps team."},"iterations":{"type":"array","items":{"type":"object","properties":{"identifier":{"type":"string","description":"The identifier of the iteration to assign."},"path":{"type":"string","description":"The path of the iteration to assign, e.g., \u0027Project/Iteration\u0027."}},"required":["identifier","path"],"additionalProperties":false},"description":"An array of iterations to assign. Each iteration must have an identifier and a path."}},"required":["project","team","iterations"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "team": "<team>",
  "iterations": null
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <team> is The name or ID of the Azure DevOps team..
   <iterations> is An array of iterations to assign. Each iteration must have an identifier and a path..

```
### build_get_definitions
- **Description**: Retrieves a list of build definitions for a given project.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"Project ID or name to get build definitions for"},"repositoryId":{"type":"string","description":"Repository ID to filter build definitions"},"repositoryType":{"type":"string","enum":["TfsGit","GitHub","BitbucketCloud"],"description":"Type of repository to filter build definitions"},"name":{"type":"string","description":"Name of the build definition to filter"},"path":{"type":"string","description":"Path of the build definition to filter"},"queryOrder":{"type":"string","enum":["None","LastModifiedAscending","LastModifiedDescending","DefinitionNameAscending","DefinitionNameDescending"],"description":"Order in which build definitions are returned"},"top":{"type":"number","description":"Maximum number of build definitions to return"},"continuationToken":{"type":"string","description":"Token for continuing paged results"},"minMetricsTime":{"type":"string","format":"date-time","description":"Minimum metrics time to filter build definitions"},"definitionIds":{"type":"array","items":{"type":"number"},"description":"Array of build definition IDs to filter"},"builtAfter":{"type":"string","format":"date-time","description":"Return definitions that have builds after this date"},"notBuiltAfter":{"type":"string","format":"date-time","description":"Return definitions that do not have builds after this date"},"includeAllProperties":{"type":"boolean","description":"Whether to include all properties in the results"},"includeLatestBuilds":{"type":"boolean","description":"Whether to include the latest builds for each definition"},"taskIdFilter":{"type":"string","description":"Task ID to filter build definitions"},"processType":{"type":"number","description":"Process type to filter build definitions"},"yamlFilename":{"type":"string","description":"YAML filename to filter build definitions"}},"required":["project"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "repositoryId": "<repositoryId>",
  "repositoryType": "<repositoryType>",
  "name": "<name>",
  "path": "<path>",
  "queryOrder": "<queryOrder>",
  "top": 42,
  "continuationToken": "<continuationToken>",
  "minMetricsTime": "<minMetricsTime>",
  "definitionIds": [0, 1, 1, 2, 3, 5, ...],
  "builtAfter": "<builtAfter>",
  "notBuiltAfter": "<notBuiltAfter>",
  "includeAllProperties": false|true,
  "includeLatestBuilds": false|true,
  "taskIdFilter": "<taskIdFilter>",
  "processType": 42,
  "yamlFilename": "<yamlFilename>"
}
Where:
   <project> is Project ID or name to get build definitions for.
   <repositoryId> is optional, and is Repository ID to filter build definitions.
   <repositoryType> is optional, and is Type of repository to filter build definitions.
   <name> is optional, and is Name of the build definition to filter.
   <path> is optional, and is Path of the build definition to filter.
   <queryOrder> is optional, and is Order in which build definitions are returned.
   <top> is optional, and is Maximum number of build definitions to return.
   <continuationToken> is optional, and is Token for continuing paged results.
   <minMetricsTime> is optional, and is Minimum metrics time to filter build definitions.
   <definitionIds> is optional, and is Array of build definition IDs to filter.
   <builtAfter> is optional, and is Return definitions that have builds after this date.
   <notBuiltAfter> is optional, and is Return definitions that do not have builds after this date.
   <includeAllProperties> is optional, and is Whether to include all properties in the results.
   <includeLatestBuilds> is optional, and is Whether to include the latest builds for each definition.
   <taskIdFilter> is optional, and is Task ID to filter build definitions.
   <processType> is optional, and is Process type to filter build definitions.
   <yamlFilename> is optional, and is YAML filename to filter build definitions.

```
### build_get_definition_revisions
- **Description**: Retrieves a list of revisions for a specific build definition.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"Project ID or name to get the build definition revisions for"},"definitionId":{"type":"number","description":"ID of the build definition to get revisions for"}},"required":["project","definitionId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "definitionId": 42
}
Where:
   <project> is Project ID or name to get the build definition revisions for.
   <definitionId> is ID of the build definition to get revisions for.

```
### build_get_builds
- **Description**: Retrieves a list of builds for a given project.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"Project ID or name to get builds for"},"definitions":{"type":"array","items":{"type":"number"},"description":"Array of build definition IDs to filter builds"},"queues":{"type":"array","items":{"type":"number"},"description":"Array of queue IDs to filter builds"},"buildNumber":{"type":"string","description":"Build number to filter builds"},"minTime":{"type":"string","format":"date-time","description":"Minimum finish time to filter builds"},"maxTime":{"type":"string","format":"date-time","description":"Maximum finish time to filter builds"},"requestedFor":{"type":"string","description":"User ID or name who requested the build"},"reasonFilter":{"type":"number","description":"Reason filter for the build (see BuildReason enum)"},"statusFilter":{"type":"number","description":"Status filter for the build (see BuildStatus enum)"},"resultFilter":{"type":"number","description":"Result filter for the build (see BuildResult enum)"},"tagFilters":{"type":"array","items":{"type":"string"},"description":"Array of tags to filter builds"},"properties":{"type":"array","items":{"type":"string"},"description":"Array of property names to include in the results"},"top":{"type":"number","description":"Maximum number of builds to return"},"continuationToken":{"type":"string","description":"Token for continuing paged results"},"maxBuildsPerDefinition":{"type":"number","description":"Maximum number of builds per definition"},"deletedFilter":{"type":"number","description":"Filter for deleted builds (see QueryDeletedOption enum)"},"queryOrder":{"type":"string","enum":["FinishTimeAscending","FinishTimeDescending","QueueTimeDescending","QueueTimeAscending","StartTimeDescending","StartTimeAscending"],"default":"QueueTimeDescending","description":"Order in which builds are returned"},"branchName":{"type":"string","description":"Branch name to filter builds"},"buildIds":{"type":"array","items":{"type":"number"},"description":"Array of build IDs to retrieve"},"repositoryId":{"type":"string","description":"Repository ID to filter builds"},"repositoryType":{"type":"string","enum":["TfsGit","GitHub","BitbucketCloud"],"description":"Type of repository to filter builds"}},"required":["project"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "definitions": [0, 1, 1, 2, 3, 5, ...],
  "queues": [0, 1, 1, 2, 3, 5, ...],
  "buildNumber": "<buildNumber>",
  "minTime": "<minTime>",
  "maxTime": "<maxTime>",
  "requestedFor": "<requestedFor>",
  "reasonFilter": 42,
  "statusFilter": 42,
  "resultFilter": 42,
  "tagFilters": ["<tagFilters1>", "<tagFilters2>", ...],
  "properties": ["<properties1>", "<properties2>", ...],
  "top": 42,
  "continuationToken": "<continuationToken>",
  "maxBuildsPerDefinition": 42,
  "deletedFilter": 42,
  "queryOrder": "<queryOrder>",
  "branchName": "<branchName>",
  "buildIds": [0, 1, 1, 2, 3, 5, ...],
  "repositoryId": "<repositoryId>",
  "repositoryType": "<repositoryType>"
}
Where:
   <project> is Project ID or name to get builds for.
   <definitions> is optional, and is Array of build definition IDs to filter builds.
   <queues> is optional, and is Array of queue IDs to filter builds.
   <buildNumber> is optional, and is Build number to filter builds.
   <minTime> is optional, and is Minimum finish time to filter builds.
   <maxTime> is optional, and is Maximum finish time to filter builds.
   <requestedFor> is optional, and is User ID or name who requested the build.
   <reasonFilter> is optional, and is Reason filter for the build (see BuildReason enum).
   <statusFilter> is optional, and is Status filter for the build (see BuildStatus enum).
   <resultFilter> is optional, and is Result filter for the build (see BuildResult enum).
   <tagFilters> is optional, and is Array of tags to filter builds.
   <properties> is optional, and is Array of property names to include in the results.
   <top> is optional, and is Maximum number of builds to return.
   <continuationToken> is optional, and is Token for continuing paged results.
   <maxBuildsPerDefinition> is optional, and is Maximum number of builds per definition.
   <deletedFilter> is optional, and is Filter for deleted builds (see QueryDeletedOption enum).
   <queryOrder> is optional, and is Order in which builds are returned.
   <branchName> is optional, and is Branch name to filter builds.
   <buildIds> is optional, and is Array of build IDs to retrieve.
   <repositoryId> is optional, and is Repository ID to filter builds.
   <repositoryType> is optional, and is Type of repository to filter builds.

```
### build_get_log
- **Description**: Retrieves the logs for a specific build.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"Project ID or name to get the build log for"},"buildId":{"type":"number","description":"ID of the build to get the log for"}},"required":["project","buildId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "buildId": 42
}
Where:
   <project> is Project ID or name to get the build log for.
   <buildId> is ID of the build to get the log for.

```
### build_get_log_by_id
- **Description**: Get a specific build log by log ID.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"Project ID or name to get the build log for"},"buildId":{"type":"number","description":"ID of the build to get the log for"},"logId":{"type":"number","description":"ID of the log to retrieve"},"startLine":{"type":"number","description":"Starting line number for the log content, defaults to 0"},"endLine":{"type":"number","description":"Ending line number for the log content, defaults to the end of the log"}},"required":["project","buildId","logId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "buildId": 42,
  "logId": 42,
  "startLine": 42,
  "endLine": 42
}
Where:
   <project> is Project ID or name to get the build log for.
   <buildId> is ID of the build to get the log for.
   <logId> is ID of the log to retrieve.
   <startLine> is optional, and is Starting line number for the log content, defaults to 0.
   <endLine> is optional, and is Ending line number for the log content, defaults to the end of the log.

```
### build_get_changes
- **Description**: Get the changes associated with a specific build.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"Project ID or name to get the build changes for"},"buildId":{"type":"number","description":"ID of the build to get changes for"},"continuationToken":{"type":"string","description":"Continuation token for pagination"},"top":{"type":"number","default":100,"description":"Number of changes to retrieve, defaults to 100"},"includeSourceChange":{"type":"boolean","description":"Whether to include source changes in the results, defaults to false"}},"required":["project","buildId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "buildId": 42,
  "continuationToken": "<continuationToken>",
  "top": 42,
  "includeSourceChange": false|true
}
Where:
   <project> is Project ID or name to get the build changes for.
   <buildId> is ID of the build to get changes for.
   <continuationToken> is optional, and is Continuation token for pagination.
   <top> is optional, and is Number of changes to retrieve, defaults to 100.
   <includeSourceChange> is optional, and is Whether to include source changes in the results, defaults to false.

```
### build_run_build
- **Description**: Triggers a new build for a specified definition.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"Project ID or name to run the build in"},"definitionId":{"type":"number","description":"ID of the build definition to run"},"sourceBranch":{"type":"string","description":"Source branch to run the build from. If not provided, the default branch will be used."},"parameters":{"type":"object","additionalProperties":{"type":"string"},"description":"Custom build parameters as key-value pairs"}},"required":["project","definitionId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "definitionId": 42,
  "sourceBranch": "<sourceBranch>",
  "parameters": null
}
Where:
   <project> is Project ID or name to run the build in.
   <definitionId> is ID of the build definition to run.
   <sourceBranch> is optional, and is Source branch to run the build from. If not provided, the default branch will be used..
   <parameters> is optional, and is Custom build parameters as key-value pairs.

```
### build_get_status
- **Description**: Fetches the status of a specific build.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"Project ID or name to get the build status for"},"buildId":{"type":"number","description":"ID of the build to get the status for"}},"required":["project","buildId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "buildId": 42
}
Where:
   <project> is Project ID or name to get the build status for.
   <buildId> is ID of the build to get the status for.

```
### build_update_build_stage
- **Description**: Updates the stage of a specific build.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"Project ID or name to update the build stage for"},"buildId":{"type":"number","description":"ID of the build to update"},"stageName":{"type":"string","description":"Name of the stage to update"},"status":{"type":"string","enum":["Cancel","Retry","Run"],"description":"New status for the stage"},"forceRetryAllJobs":{"type":"boolean","default":false,"description":"Whether to force retry all jobs in the stage."}},"required":["project","buildId","stageName","status"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "buildId": 42,
  "stageName": "<stageName>",
  "status": "<status>",
  "forceRetryAllJobs": false|true
}
Where:
   <project> is Project ID or name to update the build stage for.
   <buildId> is ID of the build to update.
   <stageName> is Name of the stage to update.
   <status> is New status for the stage.
   <forceRetryAllJobs> is optional, and is Whether to force retry all jobs in the stage..

```
### repo_create_pull_request
- **Description**: Create a new pull request.
- **Input Schema**: {"type":"object","properties":{"repositoryId":{"type":"string","description":"The ID of the repository where the pull request will be created."},"sourceRefName":{"type":"string","description":"The source branch name for the pull request, e.g., \u0027refs/heads/feature-branch\u0027."},"targetRefName":{"type":"string","description":"The target branch name for the pull request, e.g., \u0027refs/heads/main\u0027."},"title":{"type":"string","description":"The title of the pull request."},"description":{"type":"string","description":"The description of the pull request. Optional."},"isDraft":{"type":"boolean","default":false,"description":"Indicates whether the pull request is a draft. Defaults to false."},"workItems":{"type":"string","description":"Work item IDs to associate with the pull request, space-separated."}},"required":["repositoryId","sourceRefName","targetRefName","title"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "sourceRefName": "<sourceRefName>",
  "targetRefName": "<targetRefName>",
  "title": "<title>",
  "description": "<description>",
  "isDraft": false|true,
  "workItems": "<workItems>"
}
Where:
   <repositoryId> is The ID of the repository where the pull request will be created..
   <sourceRefName> is The source branch name for the pull request, e.g., 'refs/heads/feature-branch'..
   <targetRefName> is The target branch name for the pull request, e.g., 'refs/heads/main'..
   <title> is The title of the pull request..
   <description> is optional, and is The description of the pull request. Optional..
   <isDraft> is optional, and is Indicates whether the pull request is a draft. Defaults to false..
   <workItems> is optional, and is Work item IDs to associate with the pull request, space-separated..

```
### repo_update_pull_request_status
- **Description**: Update status of an existing pull request to active or abandoned.
- **Input Schema**: {"type":"object","properties":{"repositoryId":{"type":"string","description":"The ID of the repository where the pull request exists."},"pullRequestId":{"type":"number","description":"The ID of the pull request to be published."},"status":{"type":"string","enum":["Active","Abandoned"],"description":"The new status of the pull request. Can be \u0027Active\u0027 or \u0027Abandoned\u0027."}},"required":["repositoryId","pullRequestId","status"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "pullRequestId": 42,
  "status": "<status>"
}
Where:
   <repositoryId> is The ID of the repository where the pull request exists..
   <pullRequestId> is The ID of the pull request to be published..
   <status> is The new status of the pull request. Can be 'Active' or 'Abandoned'..

```
### repo_update_pull_request_reviewers
- **Description**: Add or remove reviewers for an existing pull request.
- **Input Schema**: {"type":"object","properties":{"repositoryId":{"type":"string","description":"The ID of the repository where the pull request exists."},"pullRequestId":{"type":"number","description":"The ID of the pull request to update."},"reviewerIds":{"type":"array","items":{"type":"string"},"description":"List of reviewer ids to add or remove from the pull request."},"action":{"type":"string","enum":["add","remove"],"description":"Action to perform on the reviewers. Can be \u0027add\u0027 or \u0027remove\u0027."}},"required":["repositoryId","pullRequestId","reviewerIds","action"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "pullRequestId": 42,
  "reviewerIds": ["<reviewerIds1>", "<reviewerIds2>", ...],
  "action": "<action>"
}
Where:
   <repositoryId> is The ID of the repository where the pull request exists..
   <pullRequestId> is The ID of the pull request to update..
   <reviewerIds> is List of reviewer ids to add or remove from the pull request..
   <action> is Action to perform on the reviewers. Can be 'add' or 'remove'..

```
### repo_list_repos_by_project
- **Description**: Retrieve a list of repositories for a given project
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"top":{"type":"number","default":100,"description":"The maximum number of repositories to return."},"skip":{"type":"number","default":0,"description":"The number of repositories to skip. Defaults to 0."},"repoNameFilter":{"type":"string","description":"Optional filter to search for repositories by name. If provided, only repositories with names containing this string will be returned."}},"required":["project"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "top": 42,
  "skip": 42,
  "repoNameFilter": "<repoNameFilter>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <top> is optional, and is The maximum number of repositories to return..
   <skip> is optional, and is The number of repositories to skip. Defaults to 0..
   <repoNameFilter> is optional, and is Optional filter to search for repositories by name. If provided, only repositories with names containing this string will be returned..

```
### repo_list_pull_requests_by_repo
- **Description**: Retrieve a list of pull requests for a given repository.
- **Input Schema**: {"type":"object","properties":{"repositoryId":{"type":"string","description":"The ID of the repository where the pull requests are located."},"top":{"type":"number","default":100,"description":"The maximum number of pull requests to return."},"skip":{"type":"number","default":0,"description":"The number of pull requests to skip."},"created_by_me":{"type":"boolean","default":false,"description":"Filter pull requests created by the current user."},"i_am_reviewer":{"type":"boolean","default":false,"description":"Filter pull requests where the current user is a reviewer."},"status":{"type":"string","enum":["NotSet","Active","Abandoned","Completed","All"],"default":"Active","description":"Filter pull requests by status. Defaults to \u0027Active\u0027."}},"required":["repositoryId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "top": 42,
  "skip": 42,
  "created_by_me": false|true,
  "i_am_reviewer": false|true,
  "status": "<status>"
}
Where:
   <repositoryId> is The ID of the repository where the pull requests are located..
   <top> is optional, and is The maximum number of pull requests to return..
   <skip> is optional, and is The number of pull requests to skip..
   <created_by_me> is optional, and is Filter pull requests created by the current user..
   <i_am_reviewer> is optional, and is Filter pull requests where the current user is a reviewer..
   <status> is optional, and is Filter pull requests by status. Defaults to 'Active'..

```
### repo_list_pull_requests_by_project
- **Description**: Retrieve a list of pull requests for a given project Id or Name.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"top":{"type":"number","default":100,"description":"The maximum number of pull requests to return."},"skip":{"type":"number","default":0,"description":"The number of pull requests to skip."},"created_by_me":{"type":"boolean","default":false,"description":"Filter pull requests created by the current user."},"i_am_reviewer":{"type":"boolean","default":false,"description":"Filter pull requests where the current user is a reviewer."},"status":{"type":"string","enum":["NotSet","Active","Abandoned","Completed","All"],"default":"Active","description":"Filter pull requests by status. Defaults to \u0027Active\u0027."}},"required":["project"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "top": 42,
  "skip": 42,
  "created_by_me": false|true,
  "i_am_reviewer": false|true,
  "status": "<status>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <top> is optional, and is The maximum number of pull requests to return..
   <skip> is optional, and is The number of pull requests to skip..
   <created_by_me> is optional, and is Filter pull requests created by the current user..
   <i_am_reviewer> is optional, and is Filter pull requests where the current user is a reviewer..
   <status> is optional, and is Filter pull requests by status. Defaults to 'Active'..

```
### repo_list_pull_request_threads
- **Description**: Retrieve a list of comment threads for a pull request.
- **Input Schema**: {"type":"object","properties":{"repositoryId":{"type":"string","description":"The ID of the repository where the pull request is located."},"pullRequestId":{"type":"number","description":"The ID of the pull request for which to retrieve threads."},"project":{"type":"string","description":"Project ID or project name (optional)"},"iteration":{"type":"number","description":"The iteration ID for which to retrieve threads. Optional, defaults to the latest iteration."},"baseIteration":{"type":"number","description":"The base iteration ID for which to retrieve threads. Optional, defaults to the latest base iteration."},"top":{"type":"number","default":100,"description":"The maximum number of threads to return."},"skip":{"type":"number","default":0,"description":"The number of threads to skip."},"fullResponse":{"type":"boolean","default":false,"description":"Return full thread JSON response instead of trimmed data."}},"required":["repositoryId","pullRequestId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "pullRequestId": 42,
  "project": "<project>",
  "iteration": 42,
  "baseIteration": 42,
  "top": 42,
  "skip": 42,
  "fullResponse": false|true
}
Where:
   <repositoryId> is The ID of the repository where the pull request is located..
   <pullRequestId> is The ID of the pull request for which to retrieve threads..
   <project> is optional, and is Project ID or project name (optional).
   <iteration> is optional, and is The iteration ID for which to retrieve threads. Optional, defaults to the latest iteration..
   <baseIteration> is optional, and is The base iteration ID for which to retrieve threads. Optional, defaults to the latest base iteration..
   <top> is optional, and is The maximum number of threads to return..
   <skip> is optional, and is The number of threads to skip..
   <fullResponse> is optional, and is Return full thread JSON response instead of trimmed data..

```
### repo_list_pull_request_thread_comments
- **Description**: Retrieve a list of comments in a pull request thread.
- **Input Schema**: {"type":"object","properties":{"repositoryId":{"type":"string","description":"The ID of the repository where the pull request is located."},"pullRequestId":{"type":"number","description":"The ID of the pull request for which to retrieve thread comments."},"threadId":{"type":"number","description":"The ID of the thread for which to retrieve comments."},"project":{"type":"string","description":"Project ID or project name (optional)"},"top":{"type":"number","default":100,"description":"The maximum number of comments to return."},"skip":{"type":"number","default":0,"description":"The number of comments to skip."},"fullResponse":{"type":"boolean","default":false,"description":"Return full comment JSON response instead of trimmed data."}},"required":["repositoryId","pullRequestId","threadId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "pullRequestId": 42,
  "threadId": 42,
  "project": "<project>",
  "top": 42,
  "skip": 42,
  "fullResponse": false|true
}
Where:
   <repositoryId> is The ID of the repository where the pull request is located..
   <pullRequestId> is The ID of the pull request for which to retrieve thread comments..
   <threadId> is The ID of the thread for which to retrieve comments..
   <project> is optional, and is Project ID or project name (optional).
   <top> is optional, and is The maximum number of comments to return..
   <skip> is optional, and is The number of comments to skip..
   <fullResponse> is optional, and is Return full comment JSON response instead of trimmed data..

```
### repo_list_branches_by_repo
- **Description**: Retrieve a list of branches for a given repository.
- **Input Schema**: {"type":"object","properties":{"repositoryId":{"type":"string","description":"The ID of the repository where the branches are located."},"top":{"type":"number","default":100,"description":"The maximum number of branches to return. Defaults to 100."}},"required":["repositoryId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "top": 42
}
Where:
   <repositoryId> is The ID of the repository where the branches are located..
   <top> is optional, and is The maximum number of branches to return. Defaults to 100..

```
### repo_list_my_branches_by_repo
- **Description**: Retrieve a list of my branches for a given repository Id.
- **Input Schema**: {"type":"object","properties":{"repositoryId":{"type":"string","description":"The ID of the repository where the branches are located."},"top":{"type":"number","default":100,"description":"The maximum number of branches to return."}},"required":["repositoryId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "top": 42
}
Where:
   <repositoryId> is The ID of the repository where the branches are located..
   <top> is optional, and is The maximum number of branches to return..

```
### repo_get_repo_by_name_or_id
- **Description**: Get the repository by project and repository name or ID.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"Project name or ID where the repository is located."},"repositoryNameOrId":{"type":"string","description":"Repository name or ID."}},"required":["project","repositoryNameOrId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "repositoryNameOrId": "<repositoryNameOrId>"
}
Where:
   <project> is Project name or ID where the repository is located..
   <repositoryNameOrId> is Repository name or ID..

```
### repo_get_branch_by_name
- **Description**: Get a branch by its name.
- **Input Schema**: {"type":"object","properties":{"repositoryId":{"type":"string","description":"The ID of the repository where the branch is located."},"branchName":{"type":"string","description":"The name of the branch to retrieve, e.g., \u0027main\u0027 or \u0027feature-branch\u0027."}},"required":["repositoryId","branchName"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "branchName": "<branchName>"
}
Where:
   <repositoryId> is The ID of the repository where the branch is located..
   <branchName> is The name of the branch to retrieve, e.g., 'main' or 'feature-branch'..

```
### repo_get_pull_request_by_id
- **Description**: Get a pull request by its ID.
- **Input Schema**: {"type":"object","properties":{"repositoryId":{"type":"string","description":"The ID of the repository where the pull request is located."},"pullRequestId":{"type":"number","description":"The ID of the pull request to retrieve."}},"required":["repositoryId","pullRequestId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "pullRequestId": 42
}
Where:
   <repositoryId> is The ID of the repository where the pull request is located..
   <pullRequestId> is The ID of the pull request to retrieve..

```
### repo_reply_to_comment
- **Description**: Replies to a specific comment on a pull request.
- **Input Schema**: {"type":"object","properties":{"repositoryId":{"type":"string","description":"The ID of the repository where the pull request is located."},"pullRequestId":{"type":"number","description":"The ID of the pull request where the comment thread exists."},"threadId":{"type":"number","description":"The ID of the thread to which the comment will be added."},"content":{"type":"string","description":"The content of the comment to be added."},"project":{"type":"string","description":"Project ID or project name (optional)"},"fullResponse":{"type":"boolean","default":false,"description":"Return full comment JSON response instead of a simple confirmation message."}},"required":["repositoryId","pullRequestId","threadId","content"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "pullRequestId": 42,
  "threadId": 42,
  "content": "<content>",
  "project": "<project>",
  "fullResponse": false|true
}
Where:
   <repositoryId> is The ID of the repository where the pull request is located..
   <pullRequestId> is The ID of the pull request where the comment thread exists..
   <threadId> is The ID of the thread to which the comment will be added..
   <content> is The content of the comment to be added..
   <project> is optional, and is Project ID or project name (optional).
   <fullResponse> is optional, and is Return full comment JSON response instead of a simple confirmation message..

```
### repo_create_pull_request_thread
- **Description**: Creates a new comment thread on a pull request.
- **Input Schema**: {"type":"object","properties":{"repositoryId":{"type":"string","description":"The ID of the repository where the pull request is located."},"pullRequestId":{"type":"number","description":"The ID of the pull request where the comment thread exists."},"content":{"type":"string","description":"The content of the comment to be added."},"project":{"type":"string","description":"Project ID or project name (optional)"},"filePath":{"type":"string","description":"The path of the file where the comment thread will be created. (optional)"},"rightFileStartLine":{"type":"number","description":"Position of first character of the thread\u0027s span in right file. The line number of a thread\u0027s position. Starts at 1. (optional)"},"rightFileStartOffset":{"type":"number","description":"Position of first character of the thread\u0027s span in right file. The line number of a thread\u0027s position. The character offset of a thread\u0027s position inside of a line. Starts at 1. Must only be set if rightFileStartLine is also specified. (optional)"},"rightFileEndLine":{"type":"number","description":"Position of last character of the thread\u0027s span in right file. The line number of a thread\u0027s position. Starts at 1. Must only be set if rightFileStartLine is also specified. (optional)"},"rightFileEndOffset":{"type":"number","description":"Position of last character of the thread\u0027s span in right file. The character offset of a thread\u0027s position inside of a line. Must only be set if rightFileEndLine is also specified. (optional)"}},"required":["repositoryId","pullRequestId","content"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "pullRequestId": 42,
  "content": "<content>",
  "project": "<project>",
  "filePath": "<filePath>",
  "rightFileStartLine": 42,
  "rightFileStartOffset": 42,
  "rightFileEndLine": 42,
  "rightFileEndOffset": 42
}
Where:
   <repositoryId> is The ID of the repository where the pull request is located..
   <pullRequestId> is The ID of the pull request where the comment thread exists..
   <content> is The content of the comment to be added..
   <project> is optional, and is Project ID or project name (optional).
   <filePath> is optional, and is The path of the file where the comment thread will be created. (optional).
   <rightFileStartLine> is optional, and is Position of first character of the thread's span in right file. The line number of a thread's position. Starts at 1. (optional).
   <rightFileStartOffset> is optional, and is Position of first character of the thread's span in right file. The line number of a thread's position. The character offset of a thread's position inside of a line. Starts at 1. Must only be set if rightFileStartLine is also specified. (optional).
   <rightFileEndLine> is optional, and is Position of last character of the thread's span in right file. The line number of a thread's position. Starts at 1. Must only be set if rightFileStartLine is also specified. (optional).
   <rightFileEndOffset> is optional, and is Position of last character of the thread's span in right file. The character offset of a thread's position inside of a line. Must only be set if rightFileEndLine is also specified. (optional).

```
### repo_resolve_comment
- **Description**: Resolves a specific comment thread on a pull request.
- **Input Schema**: {"type":"object","properties":{"repositoryId":{"type":"string","description":"The ID of the repository where the pull request is located."},"pullRequestId":{"type":"number","description":"The ID of the pull request where the comment thread exists."},"threadId":{"type":"number","description":"The ID of the thread to be resolved."},"fullResponse":{"type":"boolean","default":false,"description":"Return full thread JSON response instead of a simple confirmation message."}},"required":["repositoryId","pullRequestId","threadId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "pullRequestId": 42,
  "threadId": 42,
  "fullResponse": false|true
}
Where:
   <repositoryId> is The ID of the repository where the pull request is located..
   <pullRequestId> is The ID of the pull request where the comment thread exists..
   <threadId> is The ID of the thread to be resolved..
   <fullResponse> is optional, and is Return full thread JSON response instead of a simple confirmation message..

```
### repo_search_commits
- **Description**: Searches for commits in a repository
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"Project name or ID"},"repository":{"type":"string","description":"Repository name or ID"},"fromCommit":{"type":"string","description":"Starting commit ID"},"toCommit":{"type":"string","description":"Ending commit ID"},"version":{"type":"string","description":"The name of the branch, tag or commit to filter commits by"},"versionType":{"type":"string","enum":["Branch","Tag","Commit"],"default":"Branch","description":"The meaning of the version parameter, e.g., branch, tag or commit"},"skip":{"type":"number","default":0,"description":"Number of commits to skip"},"top":{"type":"number","default":10,"description":"Maximum number of commits to return"},"includeLinks":{"type":"boolean","default":false,"description":"Include commit links"},"includeWorkItems":{"type":"boolean","default":false,"description":"Include associated work items"}},"required":["project","repository"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "repository": "<repository>",
  "fromCommit": "<fromCommit>",
  "toCommit": "<toCommit>",
  "version": "<version>",
  "versionType": "<versionType>",
  "skip": 42,
  "top": 42,
  "includeLinks": false|true,
  "includeWorkItems": false|true
}
Where:
   <project> is Project name or ID.
   <repository> is Repository name or ID.
   <fromCommit> is optional, and is Starting commit ID.
   <toCommit> is optional, and is Ending commit ID.
   <version> is optional, and is The name of the branch, tag or commit to filter commits by.
   <versionType> is optional, and is The meaning of the version parameter, e.g., branch, tag or commit.
   <skip> is optional, and is Number of commits to skip.
   <top> is optional, and is Maximum number of commits to return.
   <includeLinks> is optional, and is Include commit links.
   <includeWorkItems> is optional, and is Include associated work items.

```
### repo_list_pull_requests_by_commits
- **Description**: Lists pull requests by commit IDs to find which pull requests contain specific commits
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"Project name or ID"},"repository":{"type":"string","description":"Repository name or ID"},"commits":{"type":"array","items":{"type":"string"},"description":"Array of commit IDs to query for"},"queryType":{"type":"string","enum":["NotSet","LastMergeCommit","Commit"],"default":"LastMergeCommit","description":"Type of query to perform"}},"required":["project","repository","commits"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "repository": "<repository>",
  "commits": ["<commits1>", "<commits2>", ...],
  "queryType": "<queryType>"
}
Where:
   <project> is Project name or ID.
   <repository> is Repository name or ID.
   <commits> is Array of commit IDs to query for.
   <queryType> is optional, and is Type of query to perform.

```
### wit_list_backlogs
- **Description**: Revieve a list of backlogs for a given project and team.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"team":{"type":"string","description":"The name or ID of the Azure DevOps team."}},"required":["project","team"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "team": "<team>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <team> is The name or ID of the Azure DevOps team..

```
### wit_list_backlog_work_items
- **Description**: Retrieve a list of backlogs of for a given project, team, and backlog category
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"team":{"type":"string","description":"The name or ID of the Azure DevOps team."},"backlogId":{"type":"string","description":"The ID of the backlog category to retrieve work items from."}},"required":["project","team","backlogId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "team": "<team>",
  "backlogId": "<backlogId>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <team> is The name or ID of the Azure DevOps team..
   <backlogId> is The ID of the backlog category to retrieve work items from..

```
### wit_my_work_items
- **Description**: Retrieve a list of work items relevent to the authenticated user.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"type":{"type":"string","enum":["assignedtome","myactivity"],"default":"assignedtome","description":"The type of work items to retrieve. Defaults to \u0027assignedtome\u0027."},"top":{"type":"number","default":50,"description":"The maximum number of work items to return. Defaults to 50."},"includeCompleted":{"type":"boolean","default":false,"description":"Whether to include completed work items. Defaults to false."}},"required":["project"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "type": "<type>",
  "top": 42,
  "includeCompleted": false|true
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <type> is optional, and is The type of work items to retrieve. Defaults to 'assignedtome'..
   <top> is optional, and is The maximum number of work items to return. Defaults to 50..
   <includeCompleted> is optional, and is Whether to include completed work items. Defaults to false..

```
### wit_get_work_items_batch_by_ids
- **Description**: Retrieve list of work items by IDs in batch.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"ids":{"type":"array","items":{"type":"number"},"description":"The IDs of the work items to retrieve."}},"required":["project","ids"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "ids": [0, 1, 1, 2, 3, 5, ...]
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <ids> is The IDs of the work items to retrieve..

```
### wit_get_work_item
- **Description**: Get a single work item by ID.
- **Input Schema**: {"type":"object","properties":{"id":{"type":"number","description":"The ID of the work item to retrieve."},"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"fields":{"type":"array","items":{"type":"string"},"description":"Optional list of fields to include in the response. If not provided, all fields will be returned."},"asOf":{"type":"string","format":"date-time","description":"Optional date string to retrieve the work item as of a specific time. If not provided, the current state will be returned."},"expand":{"type":"string","enum":["all","fields","links","none","relations"],"description":"Expand options include \u0027all\u0027, \u0027fields\u0027, \u0027links\u0027, \u0027none\u0027, and \u0027relations\u0027. Defaults to \u0027none\u0027."}},"required":["id","project"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "id": 42,
  "project": "<project>",
  "fields": ["<fields1>", "<fields2>", ...],
  "asOf": "<asOf>",
  "expand": "<expand>"
}
Where:
   <id> is The ID of the work item to retrieve..
   <project> is The name or ID of the Azure DevOps project..
   <fields> is optional, and is Optional list of fields to include in the response. If not provided, all fields will be returned..
   <asOf> is optional, and is Optional date string to retrieve the work item as of a specific time. If not provided, the current state will be returned..
   <expand> is optional, and is Expand options include 'all', 'fields', 'links', 'none', and 'relations'. Defaults to 'none'..

```
### wit_list_work_item_comments
- **Description**: Retrieve list of comments for a work item by ID.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"workItemId":{"type":"number","description":"The ID of the work item to retrieve comments for."},"top":{"type":"number","default":50,"description":"Optional number of comments to retrieve. Defaults to all comments."}},"required":["project","workItemId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "workItemId": 42,
  "top": 42
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <workItemId> is The ID of the work item to retrieve comments for..
   <top> is optional, and is Optional number of comments to retrieve. Defaults to all comments..

```
### wit_add_work_item_comment
- **Description**: Add comment to a work item by ID.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"workItemId":{"type":"number","description":"The ID of the work item to add a comment to."},"comment":{"type":"string","description":"The text of the comment to add to the work item."},"format":{"type":"string","enum":["markdown","html"],"default":"html"}},"required":["project","workItemId","comment"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "workItemId": 42,
  "comment": "<comment>",
  "format": "<format>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <workItemId> is The ID of the work item to add a comment to..
   <comment> is The text of the comment to add to the work item..

```
### wit_add_child_work_items
- **Description**: Create one or many child work items from a parent by work item type and parent id.
- **Input Schema**: {"type":"object","properties":{"parentId":{"type":"number","description":"The ID of the parent work item to create a child work item under."},"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"workItemType":{"type":"string","description":"The type of the child work item to create."},"items":{"type":"array","items":{"type":"object","properties":{"title":{"type":"string","description":"The title of the child work item."},"description":{"type":"string","description":"The description of the child work item."},"format":{"type":"string","enum":["Markdown","Html"],"default":"Html","description":"Format for the description on the child work item, e.g., \u0027Markdown\u0027, \u0027Html\u0027. Defaults to \u0027Html\u0027."},"areaPath":{"type":"string","description":"Optional area path for the child work item."},"iterationPath":{"type":"string","description":"Optional iteration path for the child work item."}},"required":["title","description"],"additionalProperties":false}}},"required":["parentId","project","workItemType","items"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "parentId": 42,
  "project": "<project>",
  "workItemType": "<workItemType>",
  "items": null
}
Where:
   <parentId> is The ID of the parent work item to create a child work item under..
   <project> is The name or ID of the Azure DevOps project..
   <workItemType> is The type of the child work item to create..

```
### wit_link_work_item_to_pull_request
- **Description**: Link a single work item to an existing pull request.
- **Input Schema**: {"type":"object","properties":{"projectId":{"type":"string","description":"The project ID of the Azure DevOps project (note: project name is not valid)."},"repositoryId":{"type":"string","description":"The ID of the repository containing the pull request. Do not use the repository name here, use the ID instead."},"pullRequestId":{"type":"number","description":"The ID of the pull request to link to."},"workItemId":{"type":"number","description":"The ID of the work item to link to the pull request."}},"required":["projectId","repositoryId","pullRequestId","workItemId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "projectId": "<projectId>",
  "repositoryId": "<repositoryId>",
  "pullRequestId": 42,
  "workItemId": 42
}
Where:
   <projectId> is The project ID of the Azure DevOps project (note: project name is not valid)..
   <repositoryId> is The ID of the repository containing the pull request. Do not use the repository name here, use the ID instead..
   <pullRequestId> is The ID of the pull request to link to..
   <workItemId> is The ID of the work item to link to the pull request..

```
### wit_get_work_items_for_iteration
- **Description**: Retrieve a list of work items for a specified iteration.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"team":{"type":"string","description":"The name or ID of the Azure DevOps team. If not provided, the default team will be used."},"iterationId":{"type":"string","description":"The ID of the iteration to retrieve work items for."}},"required":["project","iterationId"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "team": "<team>",
  "iterationId": "<iterationId>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <team> is optional, and is The name or ID of the Azure DevOps team. If not provided, the default team will be used..
   <iterationId> is The ID of the iteration to retrieve work items for..

```
### wit_update_work_item
- **Description**: Update a work item by ID with specified fields.
- **Input Schema**: {"type":"object","properties":{"id":{"type":"number","description":"The ID of the work item to update."},"updates":{"type":"array","items":{"type":"object","properties":{"op":{"allOf":[{"type":"string"},{"type":"string","enum":["add","replace","remove"]}],"default":"add","description":"The operation to perform on the field."},"path":{"type":"string","description":"The path of the field to update, e.g., \u0027/fields/System.Title\u0027."},"value":{"type":"string","description":"The new value for the field. This is required for \u0027Add\u0027 and \u0027Replace\u0027 operations, and should be omitted for \u0027Remove\u0027 operations."}},"required":["path","value"],"additionalProperties":false},"description":"An array of field updates to apply to the work item."}},"required":["id","updates"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "id": 42,
  "updates": null
}
Where:
   <id> is The ID of the work item to update..
   <updates> is An array of field updates to apply to the work item..

```
### wit_get_work_item_type
- **Description**: Get a specific work item type.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"workItemType":{"type":"string","description":"The name of the work item type to retrieve."}},"required":["project","workItemType"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "workItemType": "<workItemType>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <workItemType> is The name of the work item type to retrieve..

```
### wit_create_work_item
- **Description**: Create a new work item in a specified project and work item type.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"workItemType":{"type":"string","description":"The type of work item to create, e.g., \u0027Task\u0027, \u0027Bug\u0027, etc."},"fields":{"type":"array","items":{"type":"object","properties":{"name":{"type":"string","description":"The name of the field, e.g., \u0027System.Title\u0027."},"value":{"type":"string","description":"The value of the field."},"format":{"type":"string","enum":["Html","Markdown"],"description":"the format of the field value, e.g., \u0027Html\u0027, \u0027Markdown\u0027. Optional, defaults to \u0027Html\u0027."}},"required":["name","value"],"additionalProperties":false},"description":"A record of field names and values to set on the new work item. Each fild is the field name and each value is the corresponding value to set for that field."}},"required":["project","workItemType","fields"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "workItemType": "<workItemType>",
  "fields": null
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <workItemType> is The type of work item to create, e.g., 'Task', 'Bug', etc..
   <fields> is A record of field names and values to set on the new work item. Each fild is the field name and each value is the corresponding value to set for that field..

```
### wit_get_query
- **Description**: Get a query by its ID or path.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"query":{"type":"string","description":"The ID or path of the query to retrieve."},"expand":{"type":"string","enum":["None","Wiql","Clauses","All","Minimal"],"description":"Optional expand parameter to include additional details in the response. Defaults to \u0027None\u0027."},"depth":{"type":"number","default":0,"description":"Optional depth parameter to specify how deep to expand the query. Defaults to 0."},"includeDeleted":{"type":"boolean","default":false,"description":"Whether to include deleted items in the query results. Defaults to false."},"useIsoDateFormat":{"type":"boolean","default":false,"description":"Whether to use ISO date format in the response. Defaults to false."}},"required":["project","query"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "query": "<query>",
  "expand": "<expand>",
  "depth": 42,
  "includeDeleted": false|true,
  "useIsoDateFormat": false|true
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <query> is The ID or path of the query to retrieve..
   <expand> is optional, and is Optional expand parameter to include additional details in the response. Defaults to 'None'..
   <depth> is optional, and is Optional depth parameter to specify how deep to expand the query. Defaults to 0..
   <includeDeleted> is optional, and is Whether to include deleted items in the query results. Defaults to false..
   <useIsoDateFormat> is optional, and is Whether to use ISO date format in the response. Defaults to false..

```
### wit_get_query_results_by_id
- **Description**: Retrieve the results of a work item query given the query ID.
- **Input Schema**: {"type":"object","properties":{"id":{"type":"string","description":"The ID of the query to retrieve results for."},"project":{"type":"string","description":"The name or ID of the Azure DevOps project. If not provided, the default project will be used."},"team":{"type":"string","description":"The name or ID of the Azure DevOps team. If not provided, the default team will be used."},"timePrecision":{"type":"boolean","description":"Whether to include time precision in the results. Defaults to false."},"top":{"type":"number","default":50,"description":"The maximum number of results to return. Defaults to 50."}},"required":["id"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "id": "<id>",
  "project": "<project>",
  "team": "<team>",
  "timePrecision": false|true,
  "top": 42
}
Where:
   <id> is The ID of the query to retrieve results for..
   <project> is optional, and is The name or ID of the Azure DevOps project. If not provided, the default project will be used..
   <team> is optional, and is The name or ID of the Azure DevOps team. If not provided, the default team will be used..
   <timePrecision> is optional, and is Whether to include time precision in the results. Defaults to false..
   <top> is optional, and is The maximum number of results to return. Defaults to 50..

```
### wit_update_work_items_batch
- **Description**: Update work items in batch
- **Input Schema**: {"type":"object","properties":{"updates":{"type":"array","items":{"type":"object","properties":{"op":{"type":"string","enum":["Add","Replace","Remove"],"default":"Add","description":"The operation to perform on the field."},"id":{"type":"number","description":"The ID of the work item to update."},"path":{"type":"string","description":"The path of the field to update, e.g., \u0027/fields/System.Title\u0027."},"value":{"type":"string","description":"The new value for the field. This is required for \u0027add\u0027 and \u0027replace\u0027 operations, and should be omitted for \u0027remove\u0027 operations."},"format":{"type":"string","enum":["Html","Markdown"],"description":"The format of the field value. Only to be used for large text fields. e.g., \u0027Html\u0027, \u0027Markdown\u0027. Optional, defaults to \u0027Html\u0027."}},"required":["id","path","value"],"additionalProperties":false},"description":"An array of updates to apply to work items. Each update should include the operation (op), work item ID (id), field path (path), and new value (value)."}},"required":["updates"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "updates": null
}
Where:
   <updates> is An array of updates to apply to work items. Each update should include the operation (op), work item ID (id), field path (path), and new value (value)..

```
### wit_work_items_link
- **Description**: Link work items together in batch.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The name or ID of the Azure DevOps project."},"updates":{"type":"array","items":{"type":"object","properties":{"id":{"type":"number","description":"The ID of the work item to update."},"linkToId":{"type":"number","description":"The ID of the work item to link to."},"type":{"type":"string","enum":["parent","child","duplicate","duplicate of","related","successor","predecessor","tested by","tests","affects","affected by"],"default":"related","description":"Type of link to create between the work items. Options include \u0027parent\u0027, \u0027child\u0027, \u0027duplicate\u0027, \u0027duplicate of\u0027, \u0027related\u0027, \u0027successor\u0027, \u0027predecessor\u0027, \u0027tested by\u0027, \u0027tests\u0027, \u0027affects\u0027, and \u0027affected by\u0027. Defaults to \u0027related\u0027."},"comment":{"type":"string","description":"Optional comment to include with the link. This can be used to provide additional context for the link being created."}},"required":["id","linkToId"],"additionalProperties":false}}},"required":["project","updates"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "updates": null
}
Where:
   <project> is The name or ID of the Azure DevOps project..

```
### release_get_definitions
- **Description**: Retrieves list of release definitions for a given project.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"Project ID or name to get release definitions for"},"searchText":{"type":"string","description":"Search text to filter release definitions"},"expand":{"type":"string","enum":["None","Environments","Artifacts","Triggers","Variables","Tags","LastRelease"],"default":"None","description":"Expand options for release definitions"},"artifactType":{"type":"string","description":"Filter by artifact type"},"artifactSourceId":{"type":"string","description":"Filter by artifact source ID"},"top":{"type":"number","description":"Number of results to return (for pagination)"},"continuationToken":{"type":"string","description":"Continuation token for pagination"},"queryOrder":{"type":"string","enum":["IdAscending","IdDescending","NameAscending","NameDescending"],"default":"NameAscending","description":"Order of the results"},"path":{"type":"string","description":"Path to filter release definitions"},"isExactNameMatch":{"type":"boolean","default":false,"description":"Whether to match the exact name of the release definition. Default is false."},"tagFilter":{"type":"array","items":{"type":"string"},"description":"Filter by tags associated with the release definitions"},"propertyFilters":{"type":"array","items":{"type":"string"},"description":"Filter by properties associated with the release definitions"},"definitionIdFilter":{"type":"array","items":{"type":"string"},"description":"Filter by specific release definition IDs"},"isDeleted":{"type":"boolean","default":false,"description":"Whether to include deleted release definitions. Default is false."},"searchTextContainsFolderName":{"type":"boolean","description":"Whether to include folder names in the search text"}},"required":["project"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "searchText": "<searchText>",
  "expand": "<expand>",
  "artifactType": "<artifactType>",
  "artifactSourceId": "<artifactSourceId>",
  "top": 42,
  "continuationToken": "<continuationToken>",
  "queryOrder": "<queryOrder>",
  "path": "<path>",
  "isExactNameMatch": false|true,
  "tagFilter": ["<tagFilter1>", "<tagFilter2>", ...],
  "propertyFilters": ["<propertyFilters1>", "<propertyFilters2>", ...],
  "definitionIdFilter": ["<definitionIdFilter1>", "<definitionIdFilter2>", ...],
  "isDeleted": false|true,
  "searchTextContainsFolderName": false|true
}
Where:
   <project> is Project ID or name to get release definitions for.
   <searchText> is optional, and is Search text to filter release definitions.
   <expand> is optional, and is Expand options for release definitions.
   <artifactType> is optional, and is Filter by artifact type.
   <artifactSourceId> is optional, and is Filter by artifact source ID.
   <top> is optional, and is Number of results to return (for pagination).
   <continuationToken> is optional, and is Continuation token for pagination.
   <queryOrder> is optional, and is Order of the results.
   <path> is optional, and is Path to filter release definitions.
   <isExactNameMatch> is optional, and is Whether to match the exact name of the release definition. Default is false..
   <tagFilter> is optional, and is Filter by tags associated with the release definitions.
   <propertyFilters> is optional, and is Filter by properties associated with the release definitions.
   <definitionIdFilter> is optional, and is Filter by specific release definition IDs.
   <isDeleted> is optional, and is Whether to include deleted release definitions. Default is false..
   <searchTextContainsFolderName> is optional, and is Whether to include folder names in the search text.

```
### release_get_releases
- **Description**: Retrieves a list of releases for a given project.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"Project ID or name to get releases for"},"definitionId":{"type":"number","description":"ID of the release definition to filter releases"},"definitionEnvironmentId":{"type":"number","description":"ID of the definition environment to filter releases"},"searchText":{"type":"string","description":"Search text to filter releases"},"createdBy":{"type":"string","description":"User ID or name who created the release"},"statusFilter":{"type":"string","enum":["Undefined","Draft","Active","Abandoned"],"default":"Active","description":"Status of the releases to filter (default: Active)"},"environmentStatusFilter":{"type":"number","description":"Environment status to filter releases"},"minCreatedTime":{"type":"string","format":"date-time","default":"2025-07-24T00:22:15.050Z","description":"Minimum created time for releases (default: 7 days ago)"},"maxCreatedTime":{"type":"string","format":"date-time","default":"2025-07-31T00:22:15.050Z","description":"Maximum created time for releases (default: now)"},"queryOrder":{"type":"string","enum":["Descending","Ascending"],"default":"Ascending","description":"Order in which to return releases (default: Ascending)"},"top":{"type":"number","description":"Number of releases to return"},"continuationToken":{"type":"number","description":"Continuation token for pagination"},"expand":{"type":"string","enum":["None","Environments","Artifacts","Approvals","ManualInterventions","Variables","Tags"],"default":"None","description":"Expand options for releases"},"artifactTypeId":{"type":"string","description":"Filter releases by artifact type ID"},"sourceId":{"type":"string","description":"Filter releases by artifact source ID"},"artifactVersionId":{"type":"string","description":"Filter releases by artifact version ID"},"sourceBranchFilter":{"type":"string","description":"Filter releases by source branch"},"isDeleted":{"type":"boolean","default":false,"description":"Whether to include deleted releases (default: false)"},"tagFilter":{"type":"array","items":{"type":"string"},"description":"Filter releases by tags"},"propertyFilters":{"type":"array","items":{"type":"string"},"description":"Filter releases by properties"},"releaseIdFilter":{"type":"array","items":{"type":"number"},"description":"Filter by specific release IDs"},"path":{"type":"string","description":"Path to filter releases"}},"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "definitionId": 42,
  "definitionEnvironmentId": 42,
  "searchText": "<searchText>",
  "createdBy": "<createdBy>",
  "statusFilter": "<statusFilter>",
  "environmentStatusFilter": 42,
  "minCreatedTime": "<minCreatedTime>",
  "maxCreatedTime": "<maxCreatedTime>",
  "queryOrder": "<queryOrder>",
  "top": 42,
  "continuationToken": 42,
  "expand": "<expand>",
  "artifactTypeId": "<artifactTypeId>",
  "sourceId": "<sourceId>",
  "artifactVersionId": "<artifactVersionId>",
  "sourceBranchFilter": "<sourceBranchFilter>",
  "isDeleted": false|true,
  "tagFilter": ["<tagFilter1>", "<tagFilter2>", ...],
  "propertyFilters": ["<propertyFilters1>", "<propertyFilters2>", ...],
  "releaseIdFilter": [0, 1, 1, 2, 3, 5, ...],
  "path": "<path>"
}
Where:
   <project> is optional, and is Project ID or name to get releases for.
   <definitionId> is optional, and is ID of the release definition to filter releases.
   <definitionEnvironmentId> is optional, and is ID of the definition environment to filter releases.
   <searchText> is optional, and is Search text to filter releases.
   <createdBy> is optional, and is User ID or name who created the release.
   <statusFilter> is optional, and is Status of the releases to filter (default: Active).
   <environmentStatusFilter> is optional, and is Environment status to filter releases.
   <minCreatedTime> is optional, and is Minimum created time for releases (default: 7 days ago).
   <maxCreatedTime> is optional, and is Maximum created time for releases (default: now).
   <queryOrder> is optional, and is Order in which to return releases (default: Ascending).
   <top> is optional, and is Number of releases to return.
   <continuationToken> is optional, and is Continuation token for pagination.
   <expand> is optional, and is Expand options for releases.
   <artifactTypeId> is optional, and is Filter releases by artifact type ID.
   <sourceId> is optional, and is Filter releases by artifact source ID.
   <artifactVersionId> is optional, and is Filter releases by artifact version ID.
   <sourceBranchFilter> is optional, and is Filter releases by source branch.
   <isDeleted> is optional, and is Whether to include deleted releases (default: false).
   <tagFilter> is optional, and is Filter releases by tags.
   <propertyFilters> is optional, and is Filter releases by properties.
   <releaseIdFilter> is optional, and is Filter by specific release IDs.
   <path> is optional, and is Path to filter releases.

```
### wiki_get_wiki
- **Description**: Get the wiki by wikiIdentifier
- **Input Schema**: {"type":"object","properties":{"wikiIdentifier":{"type":"string","description":"The unique identifier of the wiki."},"project":{"type":"string","description":"The project name or ID where the wiki is located. If not provided, the default project will be used."}},"required":["wikiIdentifier"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "wikiIdentifier": "<wikiIdentifier>",
  "project": "<project>"
}
Where:
   <wikiIdentifier> is The unique identifier of the wiki..
   <project> is optional, and is The project name or ID where the wiki is located. If not provided, the default project will be used..

```
### wiki_list_wikis
- **Description**: Retrieve a list of wikis for an organization or project.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The project name or ID to filter wikis. If not provided, all wikis in the organization will be returned."}},"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>"
}
Where:
   <project> is optional, and is The project name or ID to filter wikis. If not provided, all wikis in the organization will be returned..

```
### wiki_list_pages
- **Description**: Retrieve a list of wiki pages for a specific wiki and project.
- **Input Schema**: {"type":"object","properties":{"wikiIdentifier":{"type":"string","description":"The unique identifier of the wiki."},"project":{"type":"string","description":"The project name or ID where the wiki is located."},"top":{"type":"number","default":20,"description":"The maximum number of pages to return. Defaults to 20."},"continuationToken":{"type":"string","description":"Token for pagination to retrieve the next set of pages."},"pageViewsForDays":{"type":"number","description":"Number of days to retrieve page views for. If not specified, page views are not included."}},"required":["wikiIdentifier","project"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "wikiIdentifier": "<wikiIdentifier>",
  "project": "<project>",
  "top": 42,
  "continuationToken": "<continuationToken>",
  "pageViewsForDays": 42
}
Where:
   <wikiIdentifier> is The unique identifier of the wiki..
   <project> is The project name or ID where the wiki is located..
   <top> is optional, and is The maximum number of pages to return. Defaults to 20..
   <continuationToken> is optional, and is Token for pagination to retrieve the next set of pages..
   <pageViewsForDays> is optional, and is Number of days to retrieve page views for. If not specified, page views are not included..

```
### wiki_get_page_content
- **Description**: Retrieve wiki page content by wikiIdentifier and path.
- **Input Schema**: {"type":"object","properties":{"wikiIdentifier":{"type":"string","description":"The unique identifier of the wiki."},"project":{"type":"string","description":"The project name or ID where the wiki is located."},"path":{"type":"string","description":"The path of the wiki page to retrieve content for."}},"required":["wikiIdentifier","project","path"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "wikiIdentifier": "<wikiIdentifier>",
  "project": "<project>",
  "path": "<path>"
}
Where:
   <wikiIdentifier> is The unique identifier of the wiki..
   <project> is The project name or ID where the wiki is located..
   <path> is The path of the wiki page to retrieve content for..

```
### testplan_list_test_plans
- **Description**: Retrieve a paginated list of test plans from an Azure DevOps project. Allows filtering for active plans and toggling detailed information.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The unique identifier (ID or name) of the Azure DevOps project."},"filterActivePlans":{"type":"boolean","default":true,"description":"Filter to include only active test plans. Defaults to true."},"includePlanDetails":{"type":"boolean","default":false,"description":"Include detailed information about each test plan."},"continuationToken":{"type":"string","description":"Token to continue fetching test plans from a previous request."}},"required":["project"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "filterActivePlans": false|true,
  "includePlanDetails": false|true,
  "continuationToken": "<continuationToken>"
}
Where:
   <project> is The unique identifier (ID or name) of the Azure DevOps project..
   <filterActivePlans> is optional, and is Filter to include only active test plans. Defaults to true..
   <includePlanDetails> is optional, and is Include detailed information about each test plan..
   <continuationToken> is optional, and is Token to continue fetching test plans from a previous request..

```
### testplan_create_test_plan
- **Description**: Creates a new test plan in the project.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The unique identifier (ID or name) of the Azure DevOps project where the test plan will be created."},"name":{"type":"string","description":"The name of the test plan to be created."},"iteration":{"type":"string","description":"The iteration path for the test plan"},"description":{"type":"string","description":"The description of the test plan"},"startDate":{"type":"string","description":"The start date of the test plan"},"endDate":{"type":"string","description":"The end date of the test plan"},"areaPath":{"type":"string","description":"The area path for the test plan"}},"required":["project","name","iteration"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "name": "<name>",
  "iteration": "<iteration>",
  "description": "<description>",
  "startDate": "<startDate>",
  "endDate": "<endDate>",
  "areaPath": "<areaPath>"
}
Where:
   <project> is The unique identifier (ID or name) of the Azure DevOps project where the test plan will be created..
   <name> is The name of the test plan to be created..
   <iteration> is The iteration path for the test plan.
   <description> is optional, and is The description of the test plan.
   <startDate> is optional, and is The start date of the test plan.
   <endDate> is optional, and is The end date of the test plan.
   <areaPath> is optional, and is The area path for the test plan.

```
### testplan_add_test_cases_to_suite
- **Description**: Adds existing test cases to a test suite.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The unique identifier (ID or name) of the Azure DevOps project."},"planId":{"type":"number","description":"The ID of the test plan."},"suiteId":{"type":"number","description":"The ID of the test suite."},"testCaseIds":{"anyOf":[{"type":"string"},{"type":"array","items":{"type":"string"}}],"description":"The ID(s) of the test case(s) to add. "}},"required":["project","planId","suiteId","testCaseIds"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "planId": 42,
  "suiteId": 42,
  "testCaseIds": "<testCaseIds>"
}
Where:
   <project> is The unique identifier (ID or name) of the Azure DevOps project..
   <planId> is The ID of the test plan..
   <suiteId> is The ID of the test suite..
   <testCaseIds> is The ID(s) of the test case(s) to add. .

```
### testplan_create_test_case
- **Description**: Creates a new test case work item.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The unique identifier (ID or name) of the Azure DevOps project."},"title":{"type":"string","description":"The title of the test case."},"steps":{"type":"string","description":"The steps to reproduce the test case. Make sure to format each step as \u00271. Step one\\n2. Step two\u0027 etc."},"priority":{"type":"number","description":"The priority of the test case."},"areaPath":{"type":"string","description":"The area path for the test case."},"iterationPath":{"type":"string","description":"The iteration path for the test case."}},"required":["project","title"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "title": "<title>",
  "steps": "<steps>",
  "priority": 42,
  "areaPath": "<areaPath>",
  "iterationPath": "<iterationPath>"
}
Where:
   <project> is The unique identifier (ID or name) of the Azure DevOps project..
   <title> is The title of the test case..
   <steps> is optional, and is The steps to reproduce the test case. Make sure to format each step as '1. Step one\n2. Step two' etc..
   <priority> is optional, and is The priority of the test case..
   <areaPath> is optional, and is The area path for the test case..
   <iterationPath> is optional, and is The iteration path for the test case..

```
### testplan_list_test_cases
- **Description**: Gets a list of test cases in the test plan.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The unique identifier (ID or name) of the Azure DevOps project."},"planid":{"type":"number","description":"The ID of the test plan."},"suiteid":{"type":"number","description":"The ID of the test suite."}},"required":["project","planid","suiteid"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "planid": 42,
  "suiteid": 42
}
Where:
   <project> is The unique identifier (ID or name) of the Azure DevOps project..
   <planid> is The ID of the test plan..
   <suiteid> is The ID of the test suite..

```
### testplan_show_test_results_from_build_id
- **Description**: Gets a list of test results for a given project and build ID.
- **Input Schema**: {"type":"object","properties":{"project":{"type":"string","description":"The unique identifier (ID or name) of the Azure DevOps project."},"buildid":{"type":"number","description":"The ID of the build."}},"required":["project","buildid"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "project": "<project>",
  "buildid": 42
}
Where:
   <project> is The unique identifier (ID or name) of the Azure DevOps project..
   <buildid> is The ID of the build..

```
### search_code
- **Description**: Search Azure DevOps Repositories for a given search text
- **Input Schema**: {"type":"object","properties":{"searchText":{"type":"string","description":"Keywords to search for in code repositories"},"project":{"type":"array","items":{"type":"string"},"description":"Filter by projects"},"repository":{"type":"array","items":{"type":"string"},"description":"Filter by repositories"},"path":{"type":"array","items":{"type":"string"},"description":"Filter by paths"},"branch":{"type":"array","items":{"type":"string"},"description":"Filter by branches"},"includeFacets":{"type":"boolean","default":false,"description":"Include facets in the search results"},"skip":{"type":"number","default":0,"description":"Number of results to skip"},"top":{"type":"number","default":5,"description":"Maximum number of results to return"}},"required":["searchText"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "searchText": "<searchText>",
  "project": ["<project1>", "<project2>", ...],
  "repository": ["<repository1>", "<repository2>", ...],
  "path": ["<path1>", "<path2>", ...],
  "branch": ["<branch1>", "<branch2>", ...],
  "includeFacets": false|true,
  "skip": 42,
  "top": 42
}
Where:
   <searchText> is Keywords to search for in code repositories.
   <project> is optional, and is Filter by projects.
   <repository> is optional, and is Filter by repositories.
   <path> is optional, and is Filter by paths.
   <branch> is optional, and is Filter by branches.
   <includeFacets> is optional, and is Include facets in the search results.
   <skip> is optional, and is Number of results to skip.
   <top> is optional, and is Maximum number of results to return.

```
### search_wiki
- **Description**: Search Azure DevOps Wiki for a given search text
- **Input Schema**: {"type":"object","properties":{"searchText":{"type":"string","description":"Keywords to search for wiki pages"},"project":{"type":"array","items":{"type":"string"},"description":"Filter by projects"},"wiki":{"type":"array","items":{"type":"string"},"description":"Filter by wiki names"},"includeFacets":{"type":"boolean","default":false,"description":"Include facets in the search results"},"skip":{"type":"number","default":0,"description":"Number of results to skip"},"top":{"type":"number","default":10,"description":"Maximum number of results to return"}},"required":["searchText"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "searchText": "<searchText>",
  "project": ["<project1>", "<project2>", ...],
  "wiki": ["<wiki1>", "<wiki2>", ...],
  "includeFacets": false|true,
  "skip": 42,
  "top": 42
}
Where:
   <searchText> is Keywords to search for wiki pages.
   <project> is optional, and is Filter by projects.
   <wiki> is optional, and is Filter by wiki names.
   <includeFacets> is optional, and is Include facets in the search results.
   <skip> is optional, and is Number of results to skip.
   <top> is optional, and is Maximum number of results to return.

```
### search_workitem
- **Description**: Get Azure DevOps Work Item search results for a given search text
- **Input Schema**: {"type":"object","properties":{"searchText":{"type":"string","description":"Search text to find in work items"},"project":{"type":"array","items":{"type":"string"},"description":"Filter by projects"},"areaPath":{"type":"array","items":{"type":"string"},"description":"Filter by area paths"},"workItemType":{"type":"array","items":{"type":"string"},"description":"Filter by work item types"},"state":{"type":"array","items":{"type":"string"},"description":"Filter by work item states"},"assignedTo":{"type":"array","items":{"type":"string"},"description":"Filter by assigned to users"},"includeFacets":{"type":"boolean","default":false,"description":"Include facets in the search results"},"skip":{"type":"number","default":0,"description":"Number of results to skip for pagination"},"top":{"type":"number","default":10,"description":"Number of results to return"}},"required":["searchText"],"additionalProperties":false,"$schema":"http://json-schema.org/draft-07/schema#"}
- **Example Input**:
```json
{
  "searchText": "<searchText>",
  "project": ["<project1>", "<project2>", ...],
  "areaPath": ["<areaPath1>", "<areaPath2>", ...],
  "workItemType": ["<workItemType1>", "<workItemType2>", ...],
  "state": ["<state1>", "<state2>", ...],
  "assignedTo": ["<assignedTo1>", "<assignedTo2>", ...],
  "includeFacets": false|true,
  "skip": 42,
  "top": 42
}
Where:
   <searchText> is Search text to find in work items.
   <project> is optional, and is Filter by projects.
   <areaPath> is optional, and is Filter by area paths.
   <workItemType> is optional, and is Filter by work item types.
   <state> is optional, and is Filter by work item states.
   <assignedTo> is optional, and is Filter by assigned to users.
   <includeFacets> is optional, and is Include facets in the search results.
   <skip> is optional, and is Number of results to skip for pagination.
   <top> is optional, and is Number of results to return.

```

