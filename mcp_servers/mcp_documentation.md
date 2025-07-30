# MCP Servers Documentation

## ADO

### Tool: core_list_project_teams
- **Description**: Retrieve a list of teams for the specified Azure DevOps project.
- **Input Type**: ADO_core_list_project_teams_669db3f1a58f42a3b55bc3a794d336be
- **Example Input**:
```json
{
  "project": "<project>",
  "mine": "<mine>",
  "top": "<top>",
  "skip": "<skip>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <mine> is optional, and is If true, only return teams that the authenticated user is a member of..
   <top> is optional, and is The maximum number of teams to return. Defaults to 100..
   <skip> is optional, and is The number of teams to skip for pagination. Defaults to 0..

```
### Tool: core_list_projects
- **Description**: Retrieve a list of projects in your Azure DevOps organization.
- **Input Type**: ADO_core_list_projects_8fb0926dcd1f4c52affccbe3ace5d978
- **Example Input**:
```json
{
  "stateFilter": "<stateFilter>",
  "top": "<top>",
  "skip": "<skip>",
  "continuationToken": "<continuationToken>",
  "projectNameFilter": "<projectNameFilter>"
}
Where:
   <stateFilter> is optional, and is Filter projects by their state. Defaults to 'wellFormed'..
   <top> is optional, and is The maximum number of projects to return. Defaults to 100..
   <skip> is optional, and is The number of projects to skip for pagination. Defaults to 0..
   <continuationToken> is optional, and is Continuation token for pagination. Used to fetch the next set of results if available..
   <projectNameFilter> is optional, and is Filter projects by name. Supports partial matches..

```
### Tool: core_get_identity_ids
- **Description**: Retrieve Azure DevOps identity IDs for a provided search filter.
- **Input Type**: ADO_core_get_identity_ids_e7c9f3a2044643c2a36f66f3aa60c321
- **Example Input**:
```json
{
  "searchFilter": "<searchFilter>"
}
Where:
   <searchFilter> is Search filter (unique namme, display name, email) to retrieve identity IDs for..

```
### Tool: work_list_team_iterations
- **Description**: Retrieve a list of iterations for a specific team in a project.
- **Input Type**: ADO_work_list_team_iterations_b1e5becaee5843059c80ede939115504
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
### Tool: work_create_iterations
- **Description**: Create new iterations in a specified Azure DevOps project.
- **Input Type**: ADO_work_create_iterations_edc336027cd34b1fbda3c10854123940
- **Example Input**:
```json
{
  "project": "<project>",
  "iterations": "<iterations>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <iterations> is An array of iterations to create. Each iteration must have a name and can optionally have start and finish dates in ISO format..

```
### Tool: work_assign_iterations
- **Description**: Assign existing iterations to a specific team in a project.
- **Input Type**: ADO_work_assign_iterations_0b7c41589dfc4e14822194ccee31c880
- **Example Input**:
```json
{
  "project": "<project>",
  "team": "<team>",
  "iterations": "<iterations>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <team> is The name or ID of the Azure DevOps team..
   <iterations> is An array of iterations to assign. Each iteration must have an identifier and a path..

```
### Tool: build_get_definitions
- **Description**: Retrieves a list of build definitions for a given project.
- **Input Type**: ADO_build_get_definitions_2c39dff648554626ae8f39954e8f64a1
- **Example Input**:
```json
{
  "project": "<project>",
  "repositoryId": "<repositoryId>",
  "repositoryType": "<repositoryType>",
  "name": "<name>",
  "path": "<path>",
  "queryOrder": "<queryOrder>",
  "top": "<top>",
  "continuationToken": "<continuationToken>",
  "minMetricsTime": "<minMetricsTime>",
  "definitionIds": "<definitionIds>",
  "builtAfter": "<builtAfter>",
  "notBuiltAfter": "<notBuiltAfter>",
  "includeAllProperties": "<includeAllProperties>",
  "includeLatestBuilds": "<includeLatestBuilds>",
  "taskIdFilter": "<taskIdFilter>",
  "processType": "<processType>",
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
### Tool: build_get_definition_revisions
- **Description**: Retrieves a list of revisions for a specific build definition.
- **Input Type**: ADO_build_get_definition_revisions_c99ac2671c454dae81c45ced8091f285
- **Example Input**:
```json
{
  "project": "<project>",
  "definitionId": "<definitionId>"
}
Where:
   <project> is Project ID or name to get the build definition revisions for.
   <definitionId> is ID of the build definition to get revisions for.

```
### Tool: build_get_builds
- **Description**: Retrieves a list of builds for a given project.
- **Input Type**: ADO_build_get_builds_a4879c591d114e1a9d126f39cce95713
- **Example Input**:
```json
{
  "project": "<project>",
  "definitions": "<definitions>",
  "queues": "<queues>",
  "buildNumber": "<buildNumber>",
  "minTime": "<minTime>",
  "maxTime": "<maxTime>",
  "requestedFor": "<requestedFor>",
  "reasonFilter": "<reasonFilter>",
  "statusFilter": "<statusFilter>",
  "resultFilter": "<resultFilter>",
  "tagFilters": "<tagFilters>",
  "properties": "<properties>",
  "top": "<top>",
  "continuationToken": "<continuationToken>",
  "maxBuildsPerDefinition": "<maxBuildsPerDefinition>",
  "deletedFilter": "<deletedFilter>",
  "queryOrder": "<queryOrder>",
  "branchName": "<branchName>",
  "buildIds": "<buildIds>",
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
### Tool: build_get_log
- **Description**: Retrieves the logs for a specific build.
- **Input Type**: ADO_build_get_log_bead8e7c25d64c46aadac7f3a9854937
- **Example Input**:
```json
{
  "project": "<project>",
  "buildId": "<buildId>"
}
Where:
   <project> is Project ID or name to get the build log for.
   <buildId> is ID of the build to get the log for.

```
### Tool: build_get_log_by_id
- **Description**: Get a specific build log by log ID.
- **Input Type**: ADO_build_get_log_by_id_195a416bd3d2487587e39f539e3af972
- **Example Input**:
```json
{
  "project": "<project>",
  "buildId": "<buildId>",
  "logId": "<logId>",
  "startLine": "<startLine>",
  "endLine": "<endLine>"
}
Where:
   <project> is Project ID or name to get the build log for.
   <buildId> is ID of the build to get the log for.
   <logId> is ID of the log to retrieve.
   <startLine> is optional, and is Starting line number for the log content, defaults to 0.
   <endLine> is optional, and is Ending line number for the log content, defaults to the end of the log.

```
### Tool: build_get_changes
- **Description**: Get the changes associated with a specific build.
- **Input Type**: ADO_build_get_changes_a59021b069314bbca315d1a7cb9b02ec
- **Example Input**:
```json
{
  "project": "<project>",
  "buildId": "<buildId>",
  "continuationToken": "<continuationToken>",
  "top": "<top>",
  "includeSourceChange": "<includeSourceChange>"
}
Where:
   <project> is Project ID or name to get the build changes for.
   <buildId> is ID of the build to get changes for.
   <continuationToken> is optional, and is Continuation token for pagination.
   <top> is optional, and is Number of changes to retrieve, defaults to 100.
   <includeSourceChange> is optional, and is Whether to include source changes in the results, defaults to false.

```
### Tool: build_run_build
- **Description**: Triggers a new build for a specified definition.
- **Input Type**: ADO_build_run_build_2fa7f248353c4bf89b92a8947b52f9de
- **Example Input**:
```json
{
  "project": "<project>",
  "definitionId": "<definitionId>",
  "sourceBranch": "<sourceBranch>",
  "parameters": "<parameters>"
}
Where:
   <project> is Project ID or name to run the build in.
   <definitionId> is ID of the build definition to run.
   <sourceBranch> is optional, and is Source branch to run the build from. If not provided, the default branch will be used..
   <parameters> is optional, and is Custom build parameters as key-value pairs.

```
### Tool: build_get_status
- **Description**: Fetches the status of a specific build.
- **Input Type**: ADO_build_get_status_5e70d7c287c64c079bb1ca8642b51f81
- **Example Input**:
```json
{
  "project": "<project>",
  "buildId": "<buildId>"
}
Where:
   <project> is Project ID or name to get the build status for.
   <buildId> is ID of the build to get the status for.

```
### Tool: build_update_build_stage
- **Description**: Updates the stage of a specific build.
- **Input Type**: ADO_build_update_build_stage_74bd9602db1d4bed867eb4694a3220ad
- **Example Input**:
```json
{
  "project": "<project>",
  "buildId": "<buildId>",
  "stageName": "<stageName>",
  "status": "<status>",
  "forceRetryAllJobs": "<forceRetryAllJobs>"
}
Where:
   <project> is Project ID or name to update the build stage for.
   <buildId> is ID of the build to update.
   <stageName> is Name of the stage to update.
   <status> is New status for the stage.
   <forceRetryAllJobs> is optional, and is Whether to force retry all jobs in the stage..

```
### Tool: repo_create_pull_request
- **Description**: Create a new pull request.
- **Input Type**: ADO_repo_create_pull_request_478fa4458d9748c5b29a525da286496b
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "sourceRefName": "<sourceRefName>",
  "targetRefName": "<targetRefName>",
  "title": "<title>",
  "description": "<description>",
  "isDraft": "<isDraft>",
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
### Tool: repo_update_pull_request_status
- **Description**: Update status of an existing pull request to active or abandoned.
- **Input Type**: ADO_repo_update_pull_request_status_df2140924b1e45aea037f1e701e0bc44
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "pullRequestId": "<pullRequestId>",
  "status": "<status>"
}
Where:
   <repositoryId> is The ID of the repository where the pull request exists..
   <pullRequestId> is The ID of the pull request to be published..
   <status> is The new status of the pull request. Can be 'Active' or 'Abandoned'..

```
### Tool: repo_update_pull_request_reviewers
- **Description**: Add or remove reviewers for an existing pull request.
- **Input Type**: ADO_repo_update_pull_request_reviewers_901caa77aebe4475a4760b4105e4df83
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "pullRequestId": "<pullRequestId>",
  "reviewerIds": "<reviewerIds>",
  "action": "<action>"
}
Where:
   <repositoryId> is The ID of the repository where the pull request exists..
   <pullRequestId> is The ID of the pull request to update..
   <reviewerIds> is List of reviewer ids to add or remove from the pull request..
   <action> is Action to perform on the reviewers. Can be 'add' or 'remove'..

```
### Tool: repo_list_repos_by_project
- **Description**: Retrieve a list of repositories for a given project
- **Input Type**: ADO_repo_list_repos_by_project_056f6e59ca9242d78a73ceef40ae845f
- **Example Input**:
```json
{
  "project": "<project>",
  "top": "<top>",
  "skip": "<skip>",
  "repoNameFilter": "<repoNameFilter>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <top> is optional, and is The maximum number of repositories to return..
   <skip> is optional, and is The number of repositories to skip. Defaults to 0..
   <repoNameFilter> is optional, and is Optional filter to search for repositories by name. If provided, only repositories with names containing this string will be returned..

```
### Tool: repo_list_pull_requests_by_repo
- **Description**: Retrieve a list of pull requests for a given repository.
- **Input Type**: ADO_repo_list_pull_requests_by_repo_3167bf8425764bad9f68beadceb83e77
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "top": "<top>",
  "skip": "<skip>",
  "created_by_me": "<created_by_me>",
  "i_am_reviewer": "<i_am_reviewer>",
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
### Tool: repo_list_pull_requests_by_project
- **Description**: Retrieve a list of pull requests for a given project Id or Name.
- **Input Type**: ADO_repo_list_pull_requests_by_project_91ac1e03d28a40ac90e679f8c5ccbab2
- **Example Input**:
```json
{
  "project": "<project>",
  "top": "<top>",
  "skip": "<skip>",
  "created_by_me": "<created_by_me>",
  "i_am_reviewer": "<i_am_reviewer>",
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
### Tool: repo_list_pull_request_threads
- **Description**: Retrieve a list of comment threads for a pull request.
- **Input Type**: ADO_repo_list_pull_request_threads_61da93b877b246cdbc0d82587171324e
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "pullRequestId": "<pullRequestId>",
  "project": "<project>",
  "iteration": "<iteration>",
  "baseIteration": "<baseIteration>",
  "top": "<top>",
  "skip": "<skip>",
  "fullResponse": "<fullResponse>"
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
### Tool: repo_list_pull_request_thread_comments
- **Description**: Retrieve a list of comments in a pull request thread.
- **Input Type**: ADO_repo_list_pull_request_thread_comments_acc945ed964f457fb3c57a8eb763da1f
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "pullRequestId": "<pullRequestId>",
  "threadId": "<threadId>",
  "project": "<project>",
  "top": "<top>",
  "skip": "<skip>",
  "fullResponse": "<fullResponse>"
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
### Tool: repo_list_branches_by_repo
- **Description**: Retrieve a list of branches for a given repository.
- **Input Type**: ADO_repo_list_branches_by_repo_8634f46ba1f745279d546c10bc903aad
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "top": "<top>"
}
Where:
   <repositoryId> is The ID of the repository where the branches are located..
   <top> is optional, and is The maximum number of branches to return. Defaults to 100..

```
### Tool: repo_list_my_branches_by_repo
- **Description**: Retrieve a list of my branches for a given repository Id.
- **Input Type**: ADO_repo_list_my_branches_by_repo_f3df459eefdd418f8aad6a9f7a88516c
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "top": "<top>"
}
Where:
   <repositoryId> is The ID of the repository where the branches are located..
   <top> is optional, and is The maximum number of branches to return..

```
### Tool: repo_get_repo_by_name_or_id
- **Description**: Get the repository by project and repository name or ID.
- **Input Type**: ADO_repo_get_repo_by_name_or_id_d47b71853d9e4f8ebb072411cd089337
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
### Tool: repo_get_branch_by_name
- **Description**: Get a branch by its name.
- **Input Type**: ADO_repo_get_branch_by_name_a76cda8560c5416ab417d2f4b631658e
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
### Tool: repo_get_pull_request_by_id
- **Description**: Get a pull request by its ID.
- **Input Type**: ADO_repo_get_pull_request_by_id_e0304de119194c548b776d727f57e4f0
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "pullRequestId": "<pullRequestId>"
}
Where:
   <repositoryId> is The ID of the repository where the pull request is located..
   <pullRequestId> is The ID of the pull request to retrieve..

```
### Tool: repo_reply_to_comment
- **Description**: Replies to a specific comment on a pull request.
- **Input Type**: ADO_repo_reply_to_comment_4e3609bd3f6b41b1965781842cee9966
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "pullRequestId": "<pullRequestId>",
  "threadId": "<threadId>",
  "content": "<content>",
  "project": "<project>",
  "fullResponse": "<fullResponse>"
}
Where:
   <repositoryId> is The ID of the repository where the pull request is located..
   <pullRequestId> is The ID of the pull request where the comment thread exists..
   <threadId> is The ID of the thread to which the comment will be added..
   <content> is The content of the comment to be added..
   <project> is optional, and is Project ID or project name (optional).
   <fullResponse> is optional, and is Return full comment JSON response instead of a simple confirmation message..

```
### Tool: repo_create_pull_request_thread
- **Description**: Creates a new comment thread on a pull request.
- **Input Type**: ADO_repo_create_pull_request_thread_4b98c6370edf4531929b3d19d019bfd0
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "pullRequestId": "<pullRequestId>",
  "content": "<content>",
  "project": "<project>",
  "filePath": "<filePath>",
  "rightFileStartLine": "<rightFileStartLine>",
  "rightFileStartOffset": "<rightFileStartOffset>",
  "rightFileEndLine": "<rightFileEndLine>",
  "rightFileEndOffset": "<rightFileEndOffset>"
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
### Tool: repo_resolve_comment
- **Description**: Resolves a specific comment thread on a pull request.
- **Input Type**: ADO_repo_resolve_comment_ba4c5f437d18492ca19df425de849e6d
- **Example Input**:
```json
{
  "repositoryId": "<repositoryId>",
  "pullRequestId": "<pullRequestId>",
  "threadId": "<threadId>",
  "fullResponse": "<fullResponse>"
}
Where:
   <repositoryId> is The ID of the repository where the pull request is located..
   <pullRequestId> is The ID of the pull request where the comment thread exists..
   <threadId> is The ID of the thread to be resolved..
   <fullResponse> is optional, and is Return full thread JSON response instead of a simple confirmation message..

```
### Tool: repo_search_commits
- **Description**: Searches for commits in a repository
- **Input Type**: ADO_repo_search_commits_3a0ab4f797564226af0ffeafe7818f3e
- **Example Input**:
```json
{
  "project": "<project>",
  "repository": "<repository>",
  "fromCommit": "<fromCommit>",
  "toCommit": "<toCommit>",
  "version": "<version>",
  "versionType": "<versionType>",
  "skip": "<skip>",
  "top": "<top>",
  "includeLinks": "<includeLinks>",
  "includeWorkItems": "<includeWorkItems>"
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
### Tool: repo_list_pull_requests_by_commits
- **Description**: Lists pull requests by commit IDs to find which pull requests contain specific commits
- **Input Type**: ADO_repo_list_pull_requests_by_commits_8364c78f6752440bbb3b401ad937a553
- **Example Input**:
```json
{
  "project": "<project>",
  "repository": "<repository>",
  "commits": "<commits>",
  "queryType": "<queryType>"
}
Where:
   <project> is Project name or ID.
   <repository> is Repository name or ID.
   <commits> is Array of commit IDs to query for.
   <queryType> is optional, and is Type of query to perform.

```
### Tool: wit_list_backlogs
- **Description**: Revieve a list of backlogs for a given project and team.
- **Input Type**: ADO_wit_list_backlogs_621c6f89b1554a018f9b730e79f016ac
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
### Tool: wit_list_backlog_work_items
- **Description**: Retrieve a list of backlogs of for a given project, team, and backlog category
- **Input Type**: ADO_wit_list_backlog_work_items_8578b74729b24018aa37a2dbab23fe59
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
### Tool: wit_my_work_items
- **Description**: Retrieve a list of work items relevent to the authenticated user.
- **Input Type**: ADO_wit_my_work_items_500fa908900a478b86884f3e336a24cb
- **Example Input**:
```json
{
  "project": "<project>",
  "type": "<type>",
  "top": "<top>",
  "includeCompleted": "<includeCompleted>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <type> is optional, and is The type of work items to retrieve. Defaults to 'assignedtome'..
   <top> is optional, and is The maximum number of work items to return. Defaults to 50..
   <includeCompleted> is optional, and is Whether to include completed work items. Defaults to false..

```
### Tool: wit_get_work_items_batch_by_ids
- **Description**: Retrieve list of work items by IDs in batch.
- **Input Type**: ADO_wit_get_work_items_batch_by_ids_c9581ef0f798493b8e201c9e7289d0aa
- **Example Input**:
```json
{
  "project": "<project>",
  "ids": "<ids>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <ids> is The IDs of the work items to retrieve..

```
### Tool: wit_get_work_item
- **Description**: Get a single work item by ID.
- **Input Type**: ADO_wit_get_work_item_4bbf666f3acf40f6b8ccf35d3224385d
- **Example Input**:
```json
{
  "id": "<id>",
  "project": "<project>",
  "fields": "<fields>",
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
### Tool: wit_list_work_item_comments
- **Description**: Retrieve list of comments for a work item by ID.
- **Input Type**: ADO_wit_list_work_item_comments_583d728b4f3645d3b6faee6f706fdcbd
- **Example Input**:
```json
{
  "project": "<project>",
  "workItemId": "<workItemId>",
  "top": "<top>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <workItemId> is The ID of the work item to retrieve comments for..
   <top> is optional, and is Optional number of comments to retrieve. Defaults to all comments..

```
### Tool: wit_add_work_item_comment
- **Description**: Add comment to a work item by ID.
- **Input Type**: ADO_wit_add_work_item_comment_0d7f21374a1048c89e52062cf590f2de
- **Example Input**:
```json
{
  "project": "<project>",
  "workItemId": "<workItemId>",
  "comment": "<comment>",
  "format": "<format>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <workItemId> is The ID of the work item to add a comment to..
   <comment> is The text of the comment to add to the work item..

```
### Tool: wit_add_child_work_items
- **Description**: Create one or many child work items from a parent by work item type and parent id.
- **Input Type**: ADO_wit_add_child_work_items_94c97caeac3245a2b07ac3772947e5a4
- **Example Input**:
```json
{
  "parentId": "<parentId>",
  "project": "<project>",
  "workItemType": "<workItemType>",
  "items": "<items>"
}
Where:
   <parentId> is The ID of the parent work item to create a child work item under..
   <project> is The name or ID of the Azure DevOps project..
   <workItemType> is The type of the child work item to create..

```
### Tool: wit_link_work_item_to_pull_request
- **Description**: Link a single work item to an existing pull request.
- **Input Type**: ADO_wit_link_work_item_to_pull_request_74256e81b82d47cbbafa1fbd2b4f1c6c
- **Example Input**:
```json
{
  "projectId": "<projectId>",
  "repositoryId": "<repositoryId>",
  "pullRequestId": "<pullRequestId>",
  "workItemId": "<workItemId>"
}
Where:
   <projectId> is The project ID of the Azure DevOps project (note: project name is not valid)..
   <repositoryId> is The ID of the repository containing the pull request. Do not use the repository name here, use the ID instead..
   <pullRequestId> is The ID of the pull request to link to..
   <workItemId> is The ID of the work item to link to the pull request..

```
### Tool: wit_get_work_items_for_iteration
- **Description**: Retrieve a list of work items for a specified iteration.
- **Input Type**: ADO_wit_get_work_items_for_iteration_df04517b4395498d8697a0e77e8d3806
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
### Tool: wit_update_work_item
- **Description**: Update a work item by ID with specified fields.
- **Input Type**: ADO_wit_update_work_item_bbc6c86c87d94b9c81c345150d8db7fc
- **Example Input**:
```json
{
  "id": "<id>",
  "updates": "<updates>"
}
Where:
   <id> is The ID of the work item to update..
   <updates> is An array of field updates to apply to the work item..

```
### Tool: wit_get_work_item_type
- **Description**: Get a specific work item type.
- **Input Type**: ADO_wit_get_work_item_type_170c925ca39a4a7188166b5984344b9c
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
### Tool: wit_create_work_item
- **Description**: Create a new work item in a specified project and work item type.
- **Input Type**: ADO_wit_create_work_item_a9e1f68343e142ab813c86a3a81ed29c
- **Example Input**:
```json
{
  "project": "<project>",
  "workItemType": "<workItemType>",
  "fields": "<fields>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <workItemType> is The type of work item to create, e.g., 'Task', 'Bug', etc..
   <fields> is A record of field names and values to set on the new work item. Each fild is the field name and each value is the corresponding value to set for that field..

```
### Tool: wit_get_query
- **Description**: Get a query by its ID or path.
- **Input Type**: ADO_wit_get_query_b075bef8c3084fbe8c4fb684230e4bd1
- **Example Input**:
```json
{
  "project": "<project>",
  "query": "<query>",
  "expand": "<expand>",
  "depth": "<depth>",
  "includeDeleted": "<includeDeleted>",
  "useIsoDateFormat": "<useIsoDateFormat>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..
   <query> is The ID or path of the query to retrieve..
   <expand> is optional, and is Optional expand parameter to include additional details in the response. Defaults to 'None'..
   <depth> is optional, and is Optional depth parameter to specify how deep to expand the query. Defaults to 0..
   <includeDeleted> is optional, and is Whether to include deleted items in the query results. Defaults to false..
   <useIsoDateFormat> is optional, and is Whether to use ISO date format in the response. Defaults to false..

```
### Tool: wit_get_query_results_by_id
- **Description**: Retrieve the results of a work item query given the query ID.
- **Input Type**: ADO_wit_get_query_results_by_id_c0629ce550a14c18adfbcea8bab204b4
- **Example Input**:
```json
{
  "id": "<id>",
  "project": "<project>",
  "team": "<team>",
  "timePrecision": "<timePrecision>",
  "top": "<top>"
}
Where:
   <id> is The ID of the query to retrieve results for..
   <project> is optional, and is The name or ID of the Azure DevOps project. If not provided, the default project will be used..
   <team> is optional, and is The name or ID of the Azure DevOps team. If not provided, the default team will be used..
   <timePrecision> is optional, and is Whether to include time precision in the results. Defaults to false..
   <top> is optional, and is The maximum number of results to return. Defaults to 50..

```
### Tool: wit_update_work_items_batch
- **Description**: Update work items in batch
- **Input Type**: ADO_wit_update_work_items_batch_3a9fac5fc846403c8a4149d53339e9c3
- **Example Input**:
```json
{
  "updates": "<updates>"
}
Where:
   <updates> is An array of updates to apply to work items. Each update should include the operation (op), work item ID (id), field path (path), and new value (value)..

```
### Tool: wit_work_items_link
- **Description**: Link work items together in batch.
- **Input Type**: ADO_wit_work_items_link_0146ee35483e4e18872acbd34eff65e1
- **Example Input**:
```json
{
  "project": "<project>",
  "updates": "<updates>"
}
Where:
   <project> is The name or ID of the Azure DevOps project..

```
### Tool: release_get_definitions
- **Description**: Retrieves list of release definitions for a given project.
- **Input Type**: ADO_release_get_definitions_3661945336924ce2afda8bcb55d2bb53
- **Example Input**:
```json
{
  "project": "<project>",
  "searchText": "<searchText>",
  "expand": "<expand>",
  "artifactType": "<artifactType>",
  "artifactSourceId": "<artifactSourceId>",
  "top": "<top>",
  "continuationToken": "<continuationToken>",
  "queryOrder": "<queryOrder>",
  "path": "<path>",
  "isExactNameMatch": "<isExactNameMatch>",
  "tagFilter": "<tagFilter>",
  "propertyFilters": "<propertyFilters>",
  "definitionIdFilter": "<definitionIdFilter>",
  "isDeleted": "<isDeleted>",
  "searchTextContainsFolderName": "<searchTextContainsFolderName>"
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
### Tool: release_get_releases
- **Description**: Retrieves a list of releases for a given project.
- **Input Type**: ADO_release_get_releases_80b374bc133c4efeab2309c54cb37925
- **Example Input**:
```json
{
  "project": "<project>",
  "definitionId": "<definitionId>",
  "definitionEnvironmentId": "<definitionEnvironmentId>",
  "searchText": "<searchText>",
  "createdBy": "<createdBy>",
  "statusFilter": "<statusFilter>",
  "environmentStatusFilter": "<environmentStatusFilter>",
  "minCreatedTime": "<minCreatedTime>",
  "maxCreatedTime": "<maxCreatedTime>",
  "queryOrder": "<queryOrder>",
  "top": "<top>",
  "continuationToken": "<continuationToken>",
  "expand": "<expand>",
  "artifactTypeId": "<artifactTypeId>",
  "sourceId": "<sourceId>",
  "artifactVersionId": "<artifactVersionId>",
  "sourceBranchFilter": "<sourceBranchFilter>",
  "isDeleted": "<isDeleted>",
  "tagFilter": "<tagFilter>",
  "propertyFilters": "<propertyFilters>",
  "releaseIdFilter": "<releaseIdFilter>",
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
### Tool: wiki_get_wiki
- **Description**: Get the wiki by wikiIdentifier
- **Input Type**: ADO_wiki_get_wiki_12defc21be1644019524a240cb6ae721
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
### Tool: wiki_list_wikis
- **Description**: Retrieve a list of wikis for an organization or project.
- **Input Type**: ADO_wiki_list_wikis_e1497accd07a48f6b42b2657c5a8567a
- **Example Input**:
```json
{
  "project": "<project>"
}
Where:
   <project> is optional, and is The project name or ID to filter wikis. If not provided, all wikis in the organization will be returned..

```
### Tool: wiki_list_pages
- **Description**: Retrieve a list of wiki pages for a specific wiki and project.
- **Input Type**: ADO_wiki_list_pages_5bdfe15fb603442fbeb2160407c3646f
- **Example Input**:
```json
{
  "wikiIdentifier": "<wikiIdentifier>",
  "project": "<project>",
  "top": "<top>",
  "continuationToken": "<continuationToken>",
  "pageViewsForDays": "<pageViewsForDays>"
}
Where:
   <wikiIdentifier> is The unique identifier of the wiki..
   <project> is The project name or ID where the wiki is located..
   <top> is optional, and is The maximum number of pages to return. Defaults to 20..
   <continuationToken> is optional, and is Token for pagination to retrieve the next set of pages..
   <pageViewsForDays> is optional, and is Number of days to retrieve page views for. If not specified, page views are not included..

```
### Tool: wiki_get_page_content
- **Description**: Retrieve wiki page content by wikiIdentifier and path.
- **Input Type**: ADO_wiki_get_page_content_a6d5650ab9b14e9bbf84827d6e951817
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
### Tool: testplan_list_test_plans
- **Description**: Retrieve a paginated list of test plans from an Azure DevOps project. Allows filtering for active plans and toggling detailed information.
- **Input Type**: ADO_testplan_list_test_plans_b7894b37197d42e092596b0027043ae8
- **Example Input**:
```json
{
  "project": "<project>",
  "filterActivePlans": "<filterActivePlans>",
  "includePlanDetails": "<includePlanDetails>",
  "continuationToken": "<continuationToken>"
}
Where:
   <project> is The unique identifier (ID or name) of the Azure DevOps project..
   <filterActivePlans> is optional, and is Filter to include only active test plans. Defaults to true..
   <includePlanDetails> is optional, and is Include detailed information about each test plan..
   <continuationToken> is optional, and is Token to continue fetching test plans from a previous request..

```
### Tool: testplan_create_test_plan
- **Description**: Creates a new test plan in the project.
- **Input Type**: ADO_testplan_create_test_plan_bf5f1cdb81044c698ff5b353673e4494
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
### Tool: testplan_add_test_cases_to_suite
- **Description**: Adds existing test cases to a test suite.
- **Input Type**: ADO_testplan_add_test_cases_to_suite_8a3e70c5e43543f9868695dc265b0117
- **Example Input**:
```json
{
  "project": "<project>",
  "planId": "<planId>",
  "suiteId": "<suiteId>",
  "testCaseIds": "<testCaseIds>"
}
Where:
   <project> is The unique identifier (ID or name) of the Azure DevOps project..
   <planId> is The ID of the test plan..
   <suiteId> is The ID of the test suite..
   <testCaseIds> is The ID(s) of the test case(s) to add. .

```
### Tool: testplan_create_test_case
- **Description**: Creates a new test case work item.
- **Input Type**: ADO_testplan_create_test_case_44f885b125124ceaa668e277fd9b3040
- **Example Input**:
```json
{
  "project": "<project>",
  "title": "<title>",
  "steps": "<steps>",
  "priority": "<priority>",
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
### Tool: testplan_list_test_cases
- **Description**: Gets a list of test cases in the test plan.
- **Input Type**: ADO_testplan_list_test_cases_ab714822a234447cab9009727db444f4
- **Example Input**:
```json
{
  "project": "<project>",
  "planid": "<planid>",
  "suiteid": "<suiteid>"
}
Where:
   <project> is The unique identifier (ID or name) of the Azure DevOps project..
   <planid> is The ID of the test plan..
   <suiteid> is The ID of the test suite..

```
### Tool: testplan_show_test_results_from_build_id
- **Description**: Gets a list of test results for a given project and build ID.
- **Input Type**: ADO_testplan_show_test_results_from_build_id_a389d5f7469f4ef58e715b95c299cecf
- **Example Input**:
```json
{
  "project": "<project>",
  "buildid": "<buildid>"
}
Where:
   <project> is The unique identifier (ID or name) of the Azure DevOps project..
   <buildid> is The ID of the build..

```
### Tool: search_code
- **Description**: Search Azure DevOps Repositories for a given search text
- **Input Type**: ADO_search_code_4d9867255b2e4c898c68caac81592f10
- **Example Input**:
```json
{
  "searchText": "<searchText>",
  "project": "<project>",
  "repository": "<repository>",
  "path": "<path>",
  "branch": "<branch>",
  "includeFacets": "<includeFacets>",
  "skip": "<skip>",
  "top": "<top>"
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
### Tool: search_wiki
- **Description**: Search Azure DevOps Wiki for a given search text
- **Input Type**: ADO_search_wiki_098d901bfe5b4baca45d8bfb7e789196
- **Example Input**:
```json
{
  "searchText": "<searchText>",
  "project": "<project>",
  "wiki": "<wiki>",
  "includeFacets": "<includeFacets>",
  "skip": "<skip>",
  "top": "<top>"
}
Where:
   <searchText> is Keywords to search for wiki pages.
   <project> is optional, and is Filter by projects.
   <wiki> is optional, and is Filter by wiki names.
   <includeFacets> is optional, and is Include facets in the search results.
   <skip> is optional, and is Number of results to skip.
   <top> is optional, and is Maximum number of results to return.

```
### Tool: search_workitem
- **Description**: Get Azure DevOps Work Item search results for a given search text
- **Input Type**: ADO_search_workitem_70abd7032d324dbdae07871f02a456df
- **Example Input**:
```json
{
  "searchText": "<searchText>",
  "project": "<project>",
  "areaPath": "<areaPath>",
  "workItemType": "<workItemType>",
  "state": "<state>",
  "assignedTo": "<assignedTo>",
  "includeFacets": "<includeFacets>",
  "skip": "<skip>",
  "top": "<top>"
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

