require 'net/http'
require 'rbconfig'

SOLUTION = 'Ehcp.slnx'
# Public Cosmos emulator key (same default as CosmosSettings); override with Cosmos__Key.
EMULATOR_KEY = 'C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw=='.freeze
BASE_URL = ENV.fetch('Ehcp__BaseUrl', 'http://localhost:5100')

def dotnet(*args)
  sh 'dotnet', *args
end

def configure_environment
  {
    'Cosmos__Endpoint' => 'https://localhost:8081',
    'Cosmos__Key' => EMULATOR_KEY,
    'Cosmos__Database' => 'ehcp-local',
    'Cosmos__Initialize' => 'true',
    'ASPNETCORE_ENVIRONMENT' => 'Development',
    'Ehcp__BaseUrl' => BASE_URL
  }.each { |name, value| ENV[name] ||= value }
  ENV['EHCP_TEST_COSMOS_KEY'] ||= ENV['Cosmos__Key']
  ENV['EHCP_TEST_COSMOS_ENDPOINT'] ||= ENV['Cosmos__Endpoint']
end

def api_ready?
  Net::HTTP.get_response(URI("#{BASE_URL}/")).is_a?(Net::HTTPSuccess)
rescue SystemCallError, IOError
  false
end

def wait_for_api(pid, timeout: 60)
  deadline = Time.now + timeout
  until api_ready?
    abort 'API process exited before becoming ready.' if Process.waitpid(pid, Process::WNOHANG)
    abort "API not ready after #{timeout}s." if Time.now > deadline
    sleep 1
  end
end

def stop_api(pid)
  if Gem.win_platform?
    system('taskkill', '/T', '/F', '/PID', pid.to_s, out: File::NULL, err: File::NULL)
  else
    Process.kill('TERM', pid)
  end
  Process.wait(pid)
rescue Errno::ESRCH, Errno::ECHILD
  nil
end

desc 'Set environment variables'
task :env do
  configure_environment
end

desc 'Restore packages'
task restore: :env do
  dotnet 'restore', SOLUTION
end

desc 'Build solution'
task build: :restore do
  dotnet 'build', SOLUTION, '--no-restore'
end

desc 'Run unit and API tests'
task unit: :build do
  dotnet 'test', 'tests/Ehcp.Tests', '--no-build'
end

desc 'Run API in the foreground'
task run: :build do
  dotnet 'run', '--no-build', '--project', 'src/Ehcp.Api'
end

desc 'Run Reqnroll specs against an already running API'
task specs: :env do
  dotnet 'test', 'tests/Ehcp.Specs', '--no-build'
end

desc 'Set env vars, build, run unit tests, start the API, run Reqnroll specs, stop the API'
task go: :unit do
  pid = Process.spawn('dotnet', 'run', '--no-build', '--project', 'src/Ehcp.Api')
  begin
    wait_for_api(pid)
    Rake::Task[:specs].invoke
  ensure
    stop_api(pid)
  end
end

task default: :go
