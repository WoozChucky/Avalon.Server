#pragma once

#include <Database/DatabaseEnv.h>
#include <Common/Types.h>
#include <filesystem>
#include <set>
#include <string>
#include <unordered_map>
#include <vector>

struct UpdateResult
{
    UpdateResult()
        : updated(0), recent(0), archived(0) { }

    UpdateResult(size_t const updated_, size_t const recent_, size_t const archived_)
        : updated(updated_), recent(recent_), archived(archived_) { }

    size_t updated;
    size_t recent;
    size_t archived;
};

class UpdateFetcher
{
    typedef std::filesystem::path Path;

public:
    UpdateFetcher(Path const& updateDirectory,
                  std::function<void(std::string const&)> const& apply,
                  std::function<void(Path const& path)> const& applyFile,
                  std::function<QueryResult(std::string const&)> const& retrieve, std::string const& dbModuleName, std::vector<std::string> const* setDirectories = nullptr);

    UpdateFetcher(Path const& updateDirectory,
        std::function<void(std::string const&)> const& apply,
        std::function<void(Path const& path)> const& applyFile,
        std::function<QueryResult(std::string const&)> const& retrieve,
        std::string const& dbModuleName,
        std::string_view modulesList = {});

    ~UpdateFetcher();

    UpdateResult Update(bool const redundancyChecks, bool const allowRehash,
                        bool const archivedRedundancy, S32 const cleanDeadReferencesMaxCount) const;

private:
    enum UpdateMode
    {
        MODE_APPLY,
        MODE_REHASH
    };

    enum State
    {
        RELEASED,
        CUSTOM,
        MODULE,
        ARCHIVED
    };

    struct AppliedFileEntry
    {
        AppliedFileEntry(std::string const& name_, std::string const& hash_, State state_, U64 timestamp_)
            : name(name_), hash(hash_), state(state_), timestamp(timestamp_) { }

        std::string const name;
        std::string const hash;
        State const state;
        U64 const timestamp;

        static inline State StateConvert(std::string const& state)
        {
            if (state == "RELEASED")
                return RELEASED;
            else if (state == "CUSTOM")
                return CUSTOM;
            else if (state == "MODULE")
                return MODULE;

            return ARCHIVED;
        }

        static inline std::string StateConvert(State const state)
        {
            switch (state)
            {
                case RELEASED:
                    return "RELEASED";
                case CUSTOM:
                    return "CUSTOM";
                case MODULE:
                    return "MODULE";
                case ARCHIVED:
                    return "ARCHIVED";
                default:
                    return "";
            }
        }

        std::string GetStateAsString() const
        {
            return StateConvert(state);
        }
    };

    struct DirectoryEntry;

    typedef std::pair<Path, State> LocaleFileEntry;

    struct PathCompare
    {
        bool operator()(LocaleFileEntry const& left, LocaleFileEntry const& right) const;
    };

    typedef std::set<LocaleFileEntry, PathCompare> LocaleFileStorage;
    typedef std::unordered_map<std::string, std::string> HashToFileNameStorage;
    typedef std::unordered_map<std::string, AppliedFileEntry> AppliedFileStorage;
    typedef std::vector<UpdateFetcher::DirectoryEntry> DirectoryStorage;

    LocaleFileStorage GetFileList() const;
    void FillFileListRecursively(Path const& path, LocaleFileStorage& storage,
                                 State const state, U32 const depth) const;

    DirectoryStorage ReceiveIncludedDirectories() const;
    AppliedFileStorage ReceiveAppliedFiles() const;

    std::string ReadSQLUpdate(Path const& file) const;

    U32 Apply(Path const& path) const;

    void UpdateEntry(AppliedFileEntry const& entry, U32 const speed = 0) const;
    void RenameEntry(std::string const& from, std::string const& to) const;
    void CleanUp(AppliedFileStorage const& storage) const;

    void UpdateState(std::string const& name, State const state) const;

    std::unique_ptr<Path> const _sourceDirectory;

    std::function<void(std::string const&)> const _apply;
    std::function<void(Path const& path)> const _applyFile;
    std::function<QueryResult(std::string const&)> const _retrieve;

    // modules
    std::string const _dbModuleName;
    std::vector<std::string> const* _setDirectories;
    std::string_view _modulesList = {};
};
